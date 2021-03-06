// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MessageTemplates.Core;
using MessageTemplates.Debugging;
using MessageTemplates.Structure;
using MessageTemplates.Parsing;
using MessageTemplates.Policies;

namespace MessageTemplates.Parameters
{
    // Values in MessageTemplates are simplified down into a lowest-common-denominator internal
    // type system so that there is a better chance of code written with one sink in
    // mind working correctly with any other. This technique also makes the programmer
    // writing a log event (roughly) in control of the cost of recording that event.
    partial class PropertyValueConverter : IMessageTemplatePropertyValueFactory
    {
        static readonly HashSet<Type> BuiltInScalarTypes = new HashSet<Type>
        {
            typeof(bool),
            typeof(char),
            typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
            typeof(string),
            typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
            typeof(Guid), typeof(Uri)
        };

        readonly IDestructuringPolicy[] _destructuringPolicies;
        readonly IScalarConversionPolicy[] _scalarConversionPolicies;
        readonly int _maximumDestructuringDepth;

        public PropertyValueConverter(int maximumDestructuringDepth, IEnumerable<Type> additionalScalarTypes, IEnumerable<IDestructuringPolicy> additionalDestructuringPolicies)
        {
            if (additionalScalarTypes == null) throw new ArgumentNullException(nameof(additionalScalarTypes));
            if (additionalDestructuringPolicies == null) throw new ArgumentNullException(nameof(additionalDestructuringPolicies));
            if (maximumDestructuringDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDestructuringDepth));

            _maximumDestructuringDepth = maximumDestructuringDepth;

            _scalarConversionPolicies = new IScalarConversionPolicy[]
            {
                new SimpleScalarConversionPolicy(BuiltInScalarTypes.Concat(additionalScalarTypes)),
                new NullableScalarConversionPolicy(),
                new EnumScalarConversionPolicy(),
                new ByteArrayScalarConversionPolicy(),
            };

            _destructuringPolicies = additionalDestructuringPolicies
                .Concat(new IDestructuringPolicy []
                {
                    new DelegateDestructuringPolicy(),
                    new ReflectionTypesScalarDestructuringPolicy()
                })
                .ToArray();
        }

        public TemplateProperty CreateProperty(string name, object value, bool destructureObjects = false)
        {
            return new TemplateProperty(name, CreatePropertyValue(value, destructureObjects));
        }

        public TemplatePropertyValue CreatePropertyValue(object value, bool destructureObjects = false)
        {
            return CreatePropertyValue(value, destructureObjects, 1);
        }

        public TemplatePropertyValue CreatePropertyValue(object value, Destructuring destructuring)
        {
            return CreatePropertyValue(value, destructuring, 1);
        }

        TemplatePropertyValue CreatePropertyValue(object value, bool destructureObjects, int depth)
        {
            return CreatePropertyValue(
                value,
                destructureObjects ?
                    Destructuring.Destructure :
                    Destructuring.Default,
                depth);
        }

        TemplatePropertyValue CreatePropertyValue(object value, Destructuring destructuring, int depth)
        {
            if (value == null)
                return new ScalarValue(null);

            if (destructuring == Destructuring.Stringify)
                return new ScalarValue(value.ToString());

            var valueType = value.GetType();
            var limiter = new DepthLimiter(depth, _maximumDestructuringDepth, this);

            foreach (var scalarConversionPolicy in _scalarConversionPolicies)
            {
                if (scalarConversionPolicy.TryConvertToScalar(value, limiter, out var converted))
                    return converted;
            }

            if (destructuring == Destructuring.Destructure)
            {
                foreach (var destructuringPolicy in _destructuringPolicies)
                {
                    if (destructuringPolicy.TryDestructure(value, limiter, out var result))
                        return result;
                }
            }

            if (value is IEnumerable enumerable)
            {
                // Only dictionaries with 'scalar' keys are permitted, as
                // more complex keys may not serialize to unique values for
                // representation in sinks. This check strengthens the expectation
                // that resulting dictionary is representable in JSON as well
                // as richer formats (e.g. XML, .NET type-aware...).
                // Only actual dictionaries are supported, as arbitrary types
                // can implement multiple IDictionary interfaces and thus introduce
                // multiple different interpretations.
                if (IsValueTypeDictionary(valueType))
                {
                    Func<object, object> getKey;
                    Func<object, object> getValue;
#if !REFLECTION_API_EVOLVED // https://blogs.msdn.microsoft.com/dotnet/2012/08/28/evolving-the-reflection-api/
                    PropertyInfo keyProperty = null;
                    getKey = o => (keyProperty ?? (keyProperty = o.GetType().GetProperty("Key"))).GetValue(o, null);
                    PropertyInfo valueProperty = null;
                    getValue = o => (valueProperty ?? (valueProperty = o.GetType().GetProperty("Value"))).GetValue(o, null);
#else
                    PropertyInfo keyProperty = null;
                    getKey = o => (keyProperty ?? (keyProperty = o.GetType().GetRuntimeProperty("Key"))).GetValue(o);
                    PropertyInfo valueProperty = null;
                    getValue = o => (valueProperty ?? (valueProperty = o.GetType().GetRuntimeProperty("Value"))).GetValue(o);
#endif

                    // TODO: stop using LINQ
                    return new DictionaryValue(enumerable
                        .Cast<object>()
                        .Where(o => o != null)
                        .Select(o => new { Key = getKey(o), Value = getValue(o) })
                        .Select(o => new KeyValuePair<ScalarValue, TemplatePropertyValue>(
                            key: (ScalarValue)limiter.CreatePropertyValue(o.Key, destructuring),
                            value: limiter.CreatePropertyValue(o.Value, destructuring))
                        )
                    );
                }

                return new SequenceValue(
                    enumerable.Cast<object>().Select(o => limiter.CreatePropertyValue(o, destructuring)));
            }

            if (destructuring == Destructuring.Destructure)
            {
                var type = value.GetType();
                var typeTag = type.Name;
#if REFLECTION_API_EVOLVED
                if (typeTag.Length <= 0 || IsCompilerGeneratedType(type))
                    typeTag = null;
#else
                if (typeTag.Length <= 0 || !char.IsLetter(typeTag[0]))
                    typeTag = null;
#endif
                return new StructureValue(GetProperties(value, limiter), typeTag);
            }

            return new ScalarValue(value.ToString());
        }

        static bool IsValueTypeDictionary(Type valueType)
        {
#if !REFLECTION_API_EVOLVED // https://blogs.msdn.microsoft.com/dotnet/2012/08/28/evolving-the-reflection-api/
            return valueType.IsGenericType &&
                   valueType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                   IsValidDictionaryKeyType(valueType.GetGenericArguments()[0]);
#else
            return valueType.IsConstructedGenericType &&
                   valueType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                   IsValidDictionaryKeyType(valueType.GenericTypeArguments[0]);
#endif
        }

        static bool IsValidDictionaryKeyType(Type valueType)
        {
            return BuiltInScalarTypes.Contains(valueType) ||
#if !REFLECTION_API_EVOLVED // https://blogs.msdn.microsoft.com/dotnet/2012/08/28/evolving-the-reflection-api/
                valueType.IsEnum;
#else
                valueType.GetTypeInfo().IsEnum;
#endif
        }

        static IEnumerable<TemplateProperty> GetProperties(
            object value, IMessageTemplatePropertyValueFactory recursive)
        {
            var properties =

#if !REFLECTION_API_EVOLVED
                // TODO: stop using LINQ
                value.GetType().GetProperties().Where(p => p.CanRead &&
                                                            p.GetGetMethod().IsPublic &&
                                                            !p.GetGetMethod().IsStatic &&
                                                            (p.Name != "Item" || p.GetIndexParameters().Length == 0));
#else
                value.GetType().GetPropertiesRecursive();
#endif
            foreach (var prop in properties)
            {
                object propValue;
                try
                {
#if !REFLECTION_API_EVOLVED // https://blogs.msdn.microsoft.com/dotnet/2012/08/28/evolving-the-reflection-api/
                    propValue = prop.GetValue(value, null);
#else
                    propValue = prop.GetValue(value);
#endif
                }
                catch (TargetParameterCountException)
                {
                    SelfLog.WriteLine("The property accessor {0} is a non-default indexer", prop);
                    continue;
                }
                catch (TargetInvocationException ex)
                {
                    SelfLog.WriteLine("The property accessor {0} threw exception {1}", prop, ex);
                    propValue = "The property accessor threw an exception: " + ex.InnerException.GetType().Name;
                }
                yield return new TemplateProperty(prop.Name, recursive.CreatePropertyValue(propValue, true));
            }
        }

#if REFLECTION_API_EVOLVED // https://blogs.msdn.microsoft.com/dotnet/2012/08/28/evolving-the-reflection-api/
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // ReSharper disable once MemberCanBePrivate.Global
        internal static bool IsCompilerGeneratedType(Type type)
        {
            var typeInfo = type.GetTypeInfo();
            var typeName = type.Name;

            //C# Anonymous types always start with "<>" and VB's start with "VB$"
            return typeInfo.IsGenericType && typeInfo.IsSealed && typeInfo.IsNotPublic && type.Namespace == null
                && (typeName[0] == '<'
                    || (typeName.Length > 2 && typeName[0] == 'V' && typeName[1] == 'B' && typeName[2] == '$'));
        }
#endif
    }
}
