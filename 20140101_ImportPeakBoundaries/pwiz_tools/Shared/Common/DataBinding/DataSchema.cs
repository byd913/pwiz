﻿/*
 * Original author: Nicholas Shulman <nicksh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2011 University of Washington - Seattle, WA
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using pwiz.Common.DataBinding.Attributes;

namespace pwiz.Common.DataBinding
{
    /// <summary>
    /// Handles property inspection on types.
    /// Applications can override this class in order to add properties to types to
    /// include user-defined properties.
    /// </summary>
    public class DataSchema
    {
        /// <summary>
        /// Returns the properties for the specified type.
        /// </summary>
        public virtual IEnumerable<PropertyDescriptor> GetPropertyDescriptors(Type type)
        {
            if (null == type)
            {
                return new PropertyDescriptor[0];
            }
            var chainParent = GetChainedPropertyDescriptorParent(type);
            if (chainParent != null)
            {
                return GetPropertyDescriptors(chainParent.PropertyType)
                    .Select(pd => (PropertyDescriptor) new ChainedPropertyDescriptor(pd.Name, chainParent, pd));
            }
            if (IsScalar(type))
            {
                return new PropertyDescriptor[0];
            }
            return ListProperties(new HashSet<string>(), type);
        }

        protected virtual IEnumerable<PropertyDescriptor> ListProperties(HashSet<string> propertyNames, Type type)
        {
            if (IsScalar(type))
            {
                return new PropertyDescriptor[0];
            }
            var propertyDescriptors = new List<PropertyDescriptor>();
            foreach (PropertyDescriptor propertyDescriptor in TypeDescriptor.GetProperties(type))
            {
                if (!propertyNames.Add(propertyDescriptor.Name))
                {
                    continue;
                }
                if (propertyDescriptor.IsBrowsable)
                {
                    propertyDescriptors.Add(propertyDescriptor);
                }
            }
            if (type.IsInterface)
            {
                foreach (var baseInterface in type.GetInterfaces())
                {
                    propertyDescriptors.AddRange(ListProperties(propertyNames, baseInterface));
                }
            }
            if (null != type.BaseType)
            {
                propertyDescriptors.AddRange(ListProperties(propertyNames, type.BaseType));
            }
            return propertyDescriptors;
        }

        /// <summary>
        /// Returns the property descriptor with the specified name.
        /// </summary>
        public virtual PropertyDescriptor GetPropertyDescriptor(Type type, string name)
        {
            return GetPropertyDescriptors(type).FirstOrDefault(pd => pd.Name == name);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public ICollectionInfo GetCollectionInfo(Type type)
        {
            return CollectionInfo.ForType(type);
        }
        /// <summary>
        /// Returns true if the property is one that can be displayed in a DataGridView.
        /// </summary>
        public virtual bool IsBrowsable(PropertyDescriptor propertyDescriptor)
        {
            if (!propertyDescriptor.IsBrowsable)
            {
                return false;
            }
            return true;
        }
        /// <summary>
        /// Returns true if the type has no properties.
        /// </summary>
        protected virtual bool IsScalar(Type type)
        {
            return type.IsPrimitive 
                || type.IsEnum
                || type == typeof (string)
                || type == typeof(DateTime);
        }

        protected PropertyDescriptor GetChainedPropertyDescriptorParent(Type type)
        {
            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(Nullable<>)
                    || genericTypeDefinition == typeof(LinkValue<>))
                {
                    return TypeDescriptor.GetProperties(type).Find("Value", false);
                }

            }
            return null;
        }
        public Type GetWrappedValueType(Type type)
        {
            var propertyDescriptor = GetChainedPropertyDescriptorParent(type);
            if (propertyDescriptor == null)
            {
                return type;
            }
            return propertyDescriptor.PropertyType;
        }
        public object UnwrapValue(object value)
        {
            if (value == null)
            {
                return null;
            }
            var propertyDescriptor = GetChainedPropertyDescriptorParent(value.GetType());
            if (propertyDescriptor != null)
            {
                return propertyDescriptor.GetValue(value);
            }
            return value;
        }

        public virtual int Compare(object o1, object o2)
        {
            if (o1 == o2)
            {
                return 0;
            }
            o1 = UnwrapLinkValue(o1);
            o2 = UnwrapLinkValue(o2);
            if (o1 is IComparable || o2 is IComparable)
            {
                return Comparer.Default.Compare(o1, o2);
            }
            if (o1 == null)
            {
                return -1;
            }
            if (o2 == null)
            {
                return 1;
            }
            return Comparer.Default.Compare(o1.ToString(), o2.ToString());
        }

        private static object UnwrapLinkValue(object o)
        {
            while (true)
            {
                var linkValue = o as ILinkValue;
                if (null == linkValue || ReferenceEquals(linkValue, o))
                {
                    return o;
                }
                o = linkValue.Value;
            }
        }

        public virtual string CaptionFromName(string name)
        {
            return name;
        }
        public virtual string CaptionFromType(Type type)
        {
            var pdChain = GetChainedPropertyDescriptorParent(type);
            if (pdChain == null)
            {
                var displayNameAttribute =
                    type.GetCustomAttributes(typeof (DisplayNameAttribute), true)
                        .Cast<DisplayNameAttribute>().FirstOrDefault();
                if (displayNameAttribute != null && !displayNameAttribute.IsDefaultAttribute())
                {
                    return displayNameAttribute.DisplayName;
                }
                return CaptionFromName(type.Name);
            }
            return CaptionFromType(pdChain.PropertyType);
        }
        public virtual bool IsRootTypeSelectable(Type type)
        {
            return typeof (ILinkValue).IsAssignableFrom(type);
        }
        public virtual string GetBaseDisplayName(ColumnDescriptor columnDescriptor)
        {
            var oneToManyColumn = columnDescriptor.GetOneToManyColumn();
            if (oneToManyColumn != null)
            {
                var oneToManyAttribute = oneToManyColumn.GetAttributes().OfType<OneToManyAttribute>().FirstOrDefault();
                if (oneToManyAttribute != null)
                {
                    if ("Key" == columnDescriptor.Name && oneToManyAttribute.IndexDisplayName != null)
                    {
                        return oneToManyAttribute.IndexDisplayName;
                    }
                    if ("Value" == columnDescriptor.Name && oneToManyAttribute.ItemDisplayName != null)
                    {
                        return oneToManyAttribute.ItemDisplayName;
                    }
                }
            }
            var displayNameAttribute = columnDescriptor.GetAttributes().OfType<DisplayNameAttribute>().FirstOrDefault();
            if (null != displayNameAttribute)
            {
                return displayNameAttribute.DisplayName;
            }
            if (columnDescriptor.Name == null)
            {
                if (columnDescriptor.Parent != null)
                {
                    return GetDisplayName(columnDescriptor.Parent);
                }
                if (columnDescriptor.PropertyType != null)
                {
                    return CaptionFromType(columnDescriptor.PropertyType);
                }
            } 
            return CaptionFromName(columnDescriptor.Name);
        }

        public virtual string FormatDisplayName(ColumnDescriptor columnDescriptor, string baseName)
        {
            return FormatChildDisplayName(columnDescriptor.Parent, baseName);
        }

        public virtual string FormatChildDisplayName(ColumnDescriptor columnDescriptor, string childDisplayName)
        {
            if (null == columnDescriptor)
            {
                return childDisplayName;
            }
            var childDisplayNameAttribute =
                columnDescriptor.GetAttributes().OfType<ChildDisplayNameAttribute>().FirstOrDefault();
                
            if (null != childDisplayNameAttribute)
            {
                childDisplayName = string.Format(childDisplayNameAttribute.Format, childDisplayName);
            }
            return FormatChildDisplayName(columnDescriptor.Parent, childDisplayName);
        }


        public virtual string GetDisplayName(ColumnDescriptor columnDescriptor)
        {
            return FormatDisplayName(columnDescriptor, GetBaseDisplayName(columnDescriptor));
        }

        public virtual string GetBaseDisplayName(DisplayColumn displayColumn)
        {
            var columnDescriptor = displayColumn.ColumnDescriptor;
            if (null == columnDescriptor)
            {
                return displayColumn.PropertyPath.ToString();
            }
            var oneToManyColumn = columnDescriptor.GetOneToManyColumn();
            if (oneToManyColumn != null)
            {
                var oneToManyAttribute = oneToManyColumn.GetAttributes().OfType<OneToManyAttribute>().FirstOrDefault();
                if (oneToManyAttribute != null)
                {
                    if ("Key" == columnDescriptor.Name && oneToManyAttribute.IndexDisplayName != null)
                    {
                        return oneToManyAttribute.IndexDisplayName;
                    }
                    if ("Value" == columnDescriptor.Name && oneToManyAttribute.ItemDisplayName != null)
                    {
                        return oneToManyAttribute.ItemDisplayName;
                    }
                }
            }
            if (columnDescriptor.Name == null && columnDescriptor.PropertyType != null)
            {
                return CaptionFromType(columnDescriptor.PropertyType);
            }
            return GetDisplayName(columnDescriptor);
        }

        public virtual bool IsAdvanced(ColumnDescriptor columnDescriptor)
        {
            if (IsObsolete(columnDescriptor))
            {
                return true;
            }
            var advancedAttribute = columnDescriptor.GetAttributes().OfType<AdvancedAttribute>().FirstOrDefault();
            if (advancedAttribute != null)
            {
                return advancedAttribute.Advanced;
            }

            var advancedWhens = columnDescriptor.GetAttributes().OfType<AdvancedWhenAttribute>().ToArray();
            var advancedIfAncestor =
                new HashSet<Type>(advancedWhens.Select(attr => attr.AncestorOfType).Where(type => null != type));
            if (advancedIfAncestor.Count > 0)
            {
                for (ColumnDescriptor ancestor = columnDescriptor.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    if (advancedIfAncestor.Any(type => type.IsAssignableFrom(ancestor.PropertyType)))
                    {
                        return true;
                    }
                }
            }

            ColumnDescriptor oneToManyColumn = columnDescriptor.GetOneToManyColumn();
            if (oneToManyColumn != null)
            {
                var oneToManyAttribute = oneToManyColumn.GetAttributes().OfType<OneToManyAttribute>().FirstOrDefault();
                if (oneToManyAttribute != null && oneToManyAttribute.ForeignKey == columnDescriptor.Name)
                {
                    return true;
                }
            }
            return false;
        }
        public virtual bool IsObsolete(ColumnDescriptor columnDescriptor)
        {
            return columnDescriptor.GetAttributes().OfType<ObsoleteAttribute>().Any();
        }
    }
}
