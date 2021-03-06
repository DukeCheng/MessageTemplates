// Copyright 2014 Serilog Contributors
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
using System.Collections.Generic;
using System.IO;
using MessageTemplates.Core;

namespace MessageTemplates.Structure
{
    /// <summary>
    /// A value represented as a mapping from keys to values.
    /// </summary>
    public class DictionaryValue : TemplatePropertyValue
    {

        /// <summary>
        /// Create a <see cref="DictionaryValue"/> with the provided <paramref name="elements"/>.
        /// </summary>
        /// <param name="elements">The key-value mappings represented in the dictionary.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public DictionaryValue(IEnumerable<KeyValuePair<ScalarValue, TemplatePropertyValue>> elements)
        {
            if (elements == null) throw new ArgumentNullException("elements");
            Elements = new ScalarTemplatePropertyValueDictionary(elements);
        }

        /// <summary>
        /// The dictionary mapping.
        /// </summary>
        public ScalarTemplatePropertyValueDictionary Elements { get; }

        /// <summary>
        /// Render the value to the output.
        /// </summary>
        /// <param name="output">The output.</param>
        /// <param name="format">A format string applied to the value, or null.</param>
        /// <param name="formatProvider">A format provider to apply to the value, or null to use the default.</param>
        /// <seealso cref="TemplatePropertyValue.ToString(string, IFormatProvider)"/>.
        public override void Render(TextWriter output, string format = null, IFormatProvider formatProvider = null)
        {
            if (output == null) throw new ArgumentNullException("output");

            output.Write('[');
            var delim = "(";
            foreach (var kvp in Elements)
            {
                output.Write(delim);
                delim = ", (";
                kvp.Key.Render(output, null, formatProvider);
                output.Write(": ");
                kvp.Value.Render(output, null, formatProvider);
                output.Write(")");
            }

            output.Write(']');
        }
    }
}
