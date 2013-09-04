﻿// ***********************************************************************
// Assembly         : ModularInputsModule
// Author           : Joel Bennett
// Created          : 03-06-2013
//
// Last Modified By : Joel Bennett
// Last Modified On : 03-07-2013
// ***********************************************************************
// <copyright file="XmlFormatter.cs" company="Splunk">
//     Copyright © 2013 by Splunk Inc., all rights reserved
// </copyright>
// <summary>
//   Splunk formatter for XML output from Modular Inputs
// </summary>
// ***********************************************************************
namespace Splunk.ModularInputs.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Management.Automation;
    using System.Security;
    using System.Text;

    using Microsoft.Practices.Unity;

    /// <summary>
    /// Splunk formatter for XML output
    /// </summary>
    public class XmlFormatter
    {
        /// <summary>
        /// The Unix Epoch time
        /// </summary>
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, new TimeSpan(0));

        /// <summary>
        /// The names of the special properties which are recognized on objects
        /// </summary>
        private static readonly HashSet<string> ReservedProperties = new HashSet<string>( new[]{ "SplunkIndex", "SplunkSource", "SplunkHost", "SplunkSourceType", "SplunkTime" });

        static XmlFormatter()
        {
            Logger = new NullLogger();
        }

        /// <summary>
        /// Gets or sets a value indicating whether to output blank values when there's an error
        /// </summary>
        /// <value><c>true</c> to output even on error; otherwise, <c>false</c>.</value>
        public static bool OutputBlanksOnError { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to log output errors.
        /// </summary>
        /// <value><c>true</c> to log errors; otherwise, <c>false</c>.</value>
        public static bool LogOutputErrors { get; set; }

        /// <summary>
        /// Gets or sets the logger used for writing output.
        /// </summary>
        /// <value>The logger.</value>
        [Dependency]
        public static ILogger Logger { get; set; }

        /// <summary>
        /// Gets a Name="Value"; representation of the data.
        /// </summary>
        /// <param name="output">The object being output.</param>
        /// <param name="stanza">A name to use for the stanza attribute</param>
        /// <param name="properties">The names of the properties to output</param>
        /// <returns>A string representation of the object.</returns>
        public static string ConvertToXml(PSObject output, string stanza, HashSet<string> properties = null)
        {
            // wrap the whole thing inside an event tag
            return "<event" + (string.IsNullOrEmpty(stanza) ? ">" : " stanza=\"" + stanza + "\">") +
                         ConvertToString(output, properties, true) +
                   "</event>\n";
        }

        /// <summary>
        /// Gets a Name="Value"; representation of the data without the &lt;event&gt; xml wrapper
        /// </summary>
        /// <param name="output">The output.</param>
        /// <param name="properties">The properties.</param>
        /// <param name="addMetadata">if set to <c>true</c>, add the metadata and encapsulate everything in xml.</param>
        /// <returns>System.String.</returns>
        public static string ConvertToString(PSObject output, HashSet<string> properties, bool addMetadata)
        {
            // If they pass in a property list, make sure it has all the ReservedProperties in it:
            if (properties != null)
            {
                properties.UnionWith(ReservedProperties);
            }
            else if (output.BaseObject is string)
            {
                // Unless otherwise specified, only output the contents and the Splunk* properties
                properties = new HashSet<string>(ReservedProperties);
            }

            IEnumerable<KeyValuePair<string, object>> values;
            if (output.BaseObject is IEnumerable<KeyValuePair<string, object>>)
            {
                values = FilterKeyValuePairs(output.BaseObject as IEnumerable<KeyValuePair<string, object>>, properties);
            }
            else if (output.BaseObject is IDictionary)
            {
                values = ConvertToKeyValuePairs(output.BaseObject as IDictionary, properties);
            }
            else
            {
                values = FilterKeyValuePairs(output.Properties, properties);
            }

            return KeyValuePairsToString(values, output.BaseObject as string, !addMetadata);
        }

        /// <summary>
        /// Filters a list of PSPropertyInfo objects to only the ones which are gettable and in the list of names
        /// </summary>
        /// <param name="output">The properties to be selected.</param>
        /// <param name="properties">An optional list of keys that we care about.</param>
        /// <returns>An enumerable collection of PSPropertyInfo objects which have Name and Value properties</returns>
        private static IEnumerable<KeyValuePair<string, object>> FilterKeyValuePairs(IEnumerable<PSPropertyInfo> output, IEnumerable<string> properties)
        {
            Debug.Assert(output != null, "output != null");

            if (properties == null)
            {
                return output.Where(p => p.MemberType != PSMemberTypes.ScriptProperty && p.IsGettable)
                    .Select(ConvertToKeyValuePair);
            }

            return output.Where( p => properties.Contains(p.Name, StringComparer.InvariantCultureIgnoreCase)
                                   && p.MemberType != PSMemberTypes.ScriptProperty && p.IsGettable)
                         .Select(ConvertToKeyValuePair);
        }

        private static KeyValuePair<string, object> ConvertToKeyValuePair(PSPropertyInfo property)
        {
            string key = null;
            object value = null;
            try
            {
                key = property.Name;
                value = property.Value;
            } catch { } // hide or ignore errors reading values... because PowerShell does. Weird?

            return string.IsNullOrEmpty(key)
                 ? new KeyValuePair<string, object>()
                 : new KeyValuePair<string, object>(key, value ?? string.Empty);
        }

        /// <summary>
        /// Converts to a filtered list of Name-Value pairs, representing some or all of the entries in output
        /// </summary>
        /// <param name="output">The objects to be converted.</param>
        /// <param name="keys">An optional list of keys that we care about.</param>
        /// <returns>An enumerable collection of dynamic objects with Name and Value properties</returns>
        private static IEnumerable<KeyValuePair<string, object>> FilterKeyValuePairs(IEnumerable<KeyValuePair<string, object>> output, IEnumerable<string> keys)
        {
            return keys == null
                ? output.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)) 
                : output.Where(kv => keys.Contains(kv.Key)).Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value));
        }

        /// <summary>
        /// Converts a Dictionary to a filtered list of Name-Value pairs representing some or all of the entries in the dictionary
        /// </summary>
        /// <param name="output">The objects to be converted.</param>
        /// <param name="keys">An optional list of keys that we care about.</param>
        /// <returns>An enumerable collection of dynamic objects with Name and Value properties</returns>
        private static IEnumerable<KeyValuePair<string, object>> ConvertToKeyValuePairs(IDictionary output, IEnumerable<string> keys)
        {
            if (keys == null)
            {
                foreach (DictionaryEntry kv in output)
                {
                    yield return new KeyValuePair<string, object>(kv.Key.ToString(), kv.Value);
                }
            }
            else
            {
                foreach (var name in keys.Where(output.Contains))
                {
                    yield return new KeyValuePair<string, object>(name, output[name]);
                }
            }
        }

        /// <summary>
        /// Converts objects with Names/Keys and Values into Name="Value" strings
        /// </summary>
        /// <param name="objects">The key/value pairs.</param>
        /// <param name="content">Extra content to be embedded below the object output</param>
        /// <param name="rawTextOnly">If set, outputs only the key=value data, with no XML metadata and no encoding</param>
        /// <returns>The string representation of the objects</returns>
        private static string KeyValuePairsToString(IEnumerable<KeyValuePair<string, object>> objects, string content = "", bool rawTextOnly = false)
        {
            bool hasTime = false;
            var meta = new StringBuilder();
            var data = new StringBuilder();

            // We always process the properties, because in PowerShell Strings can have ETS properties
            // Specifically, we might be adding Splunk* data
            // TODO: if we're in use as a cmdlet, we have a runspace, and can process Script Properties            
            var enumerator = objects.GetEnumerator();

            
                foreach (var property in objects)
                {
                    // If we can't access the name, just give up right away
                    if (!string.IsNullOrEmpty(property.Key))
                    {
                        string value = string.Empty;

                        string name = property.Key;

                        try
                        {
                            if (property.Value != null)
                            {
                                // The "O" or "o" standard format specifier represents a custom date and time format string using a pattern that preserves time zone information.
                                if (property.Value is DateTime)
                                {
                                    value = ((DateTime)property.Value).ToString("o");
                                }
                                else if (property.Value is DateTimeOffset)
                                {
                                    value = ((DateTimeOffset)property.Value).ToString("o");
                                }
                                else
                                {
                                    value = string.Format(CultureInfo.InvariantCulture, "{0}", property.Value);
                                }
                            }
                        }
                        catch
                        {
                            value = string.Empty;
                        }

                        // Handle special property names
                        if (ReservedProperties.Contains(name))
                        {
                            name = name.Remove(0, 6).ToLowerInvariant();
                            if (name.Equals("time"))
                            {
                                DateTimeOffset time;
                                try
                                {
                                    time = (DateTimeOffset)property.Value;
                                }
                                catch
                                {
                                    if (!DateTimeOffset.TryParse(value, out time))
                                    {
                                        time = DateTimeOffset.UtcNow;
                                    }
                                }

                                // convert the time to unix epoch time
                                value = (time - Epoch).TotalSeconds.ToString(CultureInfo.InvariantCulture);
                                hasTime = true;
                            }

                            meta.Insert(0, string.Format("<{0}>{1}</{0}>\n", name, value));
                        }
                        else
                        {
                            data.AppendFormat("{0}=\"{1}\"\n", name, value);
                        }
                    }
                }

            // make sure we *always* define the time
            if (!hasTime)
            {
                // convert the time to unix epoch time
                var value = (DateTimeOffset.UtcNow - Epoch).TotalSeconds.ToString(CultureInfo.InvariantCulture);
                meta.Insert(0, "<time>" + value + "</time>\n");
            }

            if (!string.IsNullOrEmpty(content))
            {
                data.Append(content);
            }

            if (rawTextOnly) { return data.ToString(); }
            return meta.ToString() + "<data>" + SecurityElement.Escape(data.ToString()) + "</data>";
        }
    }
}