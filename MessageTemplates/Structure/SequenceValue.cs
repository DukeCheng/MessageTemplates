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
    /// A value represented as an ordered sequence of values.
    /// </summary>
    public class SequenceValue : TemplatePropertyValue
    {
        /// <summary>
        /// Create a <see cref="SequenceValue"/> with the provided <paramref name="elements"/>.
        /// </summary>
        /// <param name="elements">The elements of the sequence.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public SequenceValue(IEnumerable<TemplatePropertyValue> elements)
        {
            if (elements == null) throw new ArgumentNullException("elements");
            Elements = new TemplatePropertyValueList(elements);
        }

        /// <summary>
        /// The elements of the sequence.
        /// </summary>
        public TemplatePropertyValueList Elements { get; }

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
            var allButLast = Elements.Length - 1;
            for (var i = 0; i < allButLast; ++i )
            {
                Elements[i].Render(output, format, formatProvider);
                output.Write(", ");
            }

            if (Elements.Length > 0)
                Elements[Elements.Length - 1].Render(output, format, formatProvider);

            output.Write(']');
        }
    }
}