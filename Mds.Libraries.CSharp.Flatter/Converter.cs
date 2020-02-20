using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Mds.Libraries.CSharp.Flatter
{
    public class Converter
    {
        private readonly object obj = null;


        public Converter(object _obj)
        {
            obj = _obj;
        }


        private int GetPropertiesCount(object obj, Type type = null)
        {
            var result = 0;
            foreach (var property in type.GetProperties())
            {
                if ((!property.PropertyType.IsClass || property.PropertyType == typeof(string)) && !property.PropertyType.IsConstructedGenericType)
                {
                    result++;
                }
                else
                {
                    if (property.PropertyType.IsArray)
                    {
                        if (property.GetValue(obj) is Array array)
                        {
                            if (property.PropertyType.GetElementType().IsClass && property.PropertyType != typeof(string))
                            {
                                result += array.Length * GetPropertiesCount(obj, property.PropertyType.GetElementType());
                            }
                            else
                            {
                                result += array.Length;
                            }
                        }
                        else
                        {
                            result++;
                        }
                    }
                    else if (property.PropertyType is IList list)
                    {
                        result += list.Count;
                    }
                    else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                    {
                        result += GetPropertiesCount(obj, property.PropertyType);
                    }
                }
            }

            return result;
        }

        private IEnumerable<string> GetProperties(Type type = null, object subObject = null)
        {
            var currentObject = subObject ?? obj;
            var currentType = type ?? currentObject.GetType();
            var properties = currentType.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var property in properties)
            {
                var objValue = property.GetValue(currentObject);

                //если свойство является примитивным типом данных
                if (property.PropertyType.Name != property.Name && (!property.PropertyType.IsClass || property.PropertyType == typeof(string)))
                {
                    yield return property.Name;
                }

                if (objValue is Array array)
                {
                    for (var i = 0; i < array.Length; i++)
                    {
                        var arrayValue = array.GetValue(i);
                        var elementType = property.PropertyType.GetElementType();

                        //если свойство является классом со своими свойствами, вызываем рекурсию
                        if (arrayValue != null && elementType.IsClass && elementType != typeof(string))
                        {
                            foreach (var subProperty in GetProperties(elementType, arrayValue))
                            {
                                yield return $"{property.Name}[{i}].{subProperty}";
                            }
                        }
                        else
                        {
                            yield return $"{property.Name}[{i}]";
                        }
                    }
                    continue;
                }

                //если свойство является коллекцией
                else if (objValue is IList list)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        yield return $"{property.Name}[{i}]";
                    }
                    continue;
                }

                //если свойство является классом со своими свойствами, вызываем рекурсию
                else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                {
                    foreach (var subProperty in GetProperties(property.PropertyType, objValue))
                    {
                        yield return $"{property.Name}.{subProperty}";
                    }
                }
            }
        }

        private IEnumerable<object> GetPropertiesValues(Type type = null, object subObject = null)
        {
            var currentType = type ?? obj.GetType();
            var currentObject = subObject ?? obj;
            var properties = currentType.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var property in properties)
            {
                var objValue = property.GetValue(currentObject);
                if (property.PropertyType.Name != property.Name && (!property.PropertyType.IsClass || property.PropertyType == typeof(string)))
                {
                    yield return objValue;
                }

                else if (property.PropertyType.IsArray && objValue is Array array)
                {
                    foreach (var item in array)
                    {
                        //если свойство является классом с пользовательскими свойствами, вызываем рекурсию
                        if (item.GetType().IsClass && item.GetType() != typeof(string))
                        {
                            foreach (var subProperty in GetPropertiesValues(item.GetType(), item))
                            {
                                yield return subProperty;
                            }
                        }
                        else
                        {
                            yield return item;
                        }
                    }
                    continue;
                }

                else if (property.PropertyType.IsConstructedGenericType && objValue is IList list)
                {
                    foreach (var item in list)
                    {
                        yield return item;
                    }
                }

                else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                {
                    foreach (var subProperty in GetPropertiesValues(property.PropertyType, objValue))
                    {
                        yield return subProperty;
                    }
                }
            }
        }

        public (IEnumerable<string> headers, IEnumerable<object> values) Serialize()
        {
            return (GetProperties(), GetPropertiesValues());
        }

        public object Deserialize<T>(IEnumerable<string> headers, IEnumerable<object> values)
        {
            var objType = typeof(T);
            var deserializedObject = Activator.CreateInstance(objType);
            var listHeaders = new List<string>(headers);
            var listValues = new List<object>(values);
            var index = 0;

            for (var i = 0; i < headers.Count(); i++)
            {
                var property = default(PropertyInfo);
                var propertyName = listHeaders[i].Split('.').First();

                //массив примитивов
                if (propertyName.Contains(']') && !listHeaders[i].Contains('.'))
                {
                    propertyName = listHeaders[i].Substring(0, listHeaders[i].IndexOf('['));
                }

                //массив объектов 
                else if (listHeaders[i].Contains(']') && listHeaders[i].Contains('.'))
                {
                    //если это массив объектов, удаляем последние три символа (скобки с индексом)
                    if (propertyName.EndsWith("]"))
                    {
                        propertyName = string.Join(null, propertyName.SkipLast(3));
                    }
                }

                property = objType.GetProperty(propertyName);
                if (property != null)
                {
                    if (!property.PropertyType.IsClass)
                    {
                        var typeConverter = TypeDescriptor.GetConverter(property.PropertyType);
                        var propValue = typeConverter.ConvertFromString($"{listValues[i]}");

                        property.SetValue(deserializedObject, propValue);
                    }
                    else
                    {
                        var objValue = property.GetValue(deserializedObject);

                        if (property.PropertyType.IsClass && !property.PropertyType.IsArray && !property.PropertyType.IsGenericType)
                        {
                            if (objValue == null)
                            {
                                objValue = Activator.CreateInstance(property.PropertyType);
                            }
                            property.SetValue(deserializedObject, objValue);
                            var propCount = GetPropertiesCount(objValue, property.PropertyType);
                            var resultObject = GetSubObject(property, listHeaders, listValues, i);
                            property.SetValue(deserializedObject, resultObject);
                            propCount = GetPropertiesCount(resultObject, property.PropertyType);
                            i += propCount - 1;
                        }
                        //если свойство является не пользовательским классом (e.g. List, Dictionary, Array)
                        else
                        {
                            if (property.PropertyType.IsArray)
                            {
                                var elementType = property.PropertyType.GetElementType();
                                var actualValues = default(Array);

                                if (elementType.IsClass)
                                {
                                    var arrayLength = headers.Count(header => header.Contains($"{objType.Name}.{property.Name}[") || header.StartsWith($"{property.Name}["));
                                    var resultObjectPropertiesCount = GetPropertiesCount(Activator.CreateInstance(elementType), elementType);
                                    arrayLength /= resultObjectPropertiesCount;
                                    var resultObject = GetSubObject(property, listHeaders, listValues, i, i + resultObjectPropertiesCount);

                                    if (objValue == null)
                                    {
                                        objValue = Array.CreateInstance(elementType, arrayLength);
                                        index = 0;
                                    }
                                    property.SetValue(deserializedObject, objValue);

                                    (property.GetValue(deserializedObject) as Array).SetValue(resultObject, index);
                                    index++;
                                    i += resultObjectPropertiesCount - 1;
                                }
                                else
                                {
                                    var arrayLength = headers.Count(header => header.Contains($"{objType.Name}.{property.Name}[") || header.StartsWith($"{property.Name}["));
                                    actualValues = Array.CreateInstance(elementType, arrayLength);
                                    var valueIndex = i;
                                    for (var k = 0; k < arrayLength; k++)
                                    {
                                        actualValues.SetValue(Convert.ChangeType(listValues[valueIndex++], elementType), k);
                                    }
                                    property.SetValue(deserializedObject, actualValues);
                                    i += arrayLength - 1;
                                }
                            }

                            else if (property.PropertyType.IsGenericType && objValue is IList)
                            {
                                var typeConverter = TypeDescriptor.GetConverter(listValues[i].GetType());
                                var propValue = typeConverter.ConvertFromString($"{listValues[i]}");

                                (objValue as IList).Add(propValue);
                                property.SetValue(deserializedObject, objValue);
                            }
                        }
                    }
                }
            }

            return deserializedObject;
        }

        private object GetSubObject(PropertyInfo propertyInfo, List<string> headers, List<object> values, int startIndex = 0, int finish = 0)
        {
            var objType = propertyInfo.PropertyType;
            var index = 0;

            if (propertyInfo.PropertyType.IsArray)
            {
                objType = propertyInfo.PropertyType.GetElementType();
            }

            var subObject = Activator.CreateInstance(objType);

            if (finish == 0)
            {
                finish = headers.Count;
            }

            for (var i = startIndex; i < finish; i++)
            {
                var property = default(PropertyInfo);

                foreach (var item in objType.GetProperties())
                {
                    if (headers[i].Contains($"{propertyInfo.Name}.{item.Name}"))
                    {
                        property = item;
                        break;
                    }
                    else if (propertyInfo.PropertyType.IsArray)
                    {
                        var propertyValueIndex = headers[i].IndexOf($"{item.Name}");
                        var propertyIndex = (headers[i].IndexOf($"{propertyInfo.Name}[") + propertyInfo.Name.Length);

                        if (0 <= (propertyValueIndex - propertyIndex) && (propertyValueIndex - propertyIndex) <= 4)
                        {
                            property = item;
                            break;
                        }
                    }

                }

                if (property != null)
                {
                    var objValue = property.GetValue(subObject);

                    if (property.PropertyType.IsArray)
                    {
                        var elementType = property.PropertyType.GetElementType();

                        var actualValues = default(Array);
                        if (elementType.IsClass)
                        {
                            var arrayLength = headers.Count(header => header.Contains($"{objType.Name}.{property.Name}[") || header.StartsWith($"{property.Name}["));
                            var resultObjectPropertiesCount = GetPropertiesCount(Activator.CreateInstance(elementType), elementType);
                            arrayLength /= resultObjectPropertiesCount;
                            var resultObject = GetSubObject(property, headers, values, i, i + resultObjectPropertiesCount);

                            if (objValue == null)
                            {
                                objValue = Array.CreateInstance(elementType, arrayLength);
                                index = 0;
                            }
                            property.SetValue(subObject, objValue);

                            (property.GetValue(subObject) as Array).SetValue(resultObject, index);
                            index++;
                            i += resultObjectPropertiesCount - 1;
                        }
                        else
                        {
                            actualValues = Array.CreateInstance(elementType, (objValue as Array).Length);
                            var valueIndex = i;
                            for (var k = 0; k < (objValue as Array).Length; k++)
                            {
                                actualValues.SetValue(Convert.ChangeType(values[valueIndex++], elementType), k);
                            }
                            property.SetValue(subObject, actualValues);
                            i += (objValue as Array).Length;
                        }

                    }

                    else if (property.PropertyType.IsGenericType && objValue is IList)
                    {
                        var typeConverter = TypeDescriptor.GetConverter(values[i].GetType());
                        var propValue = typeConverter.ConvertFromString($"{values[i]}");

                        (objValue as IList).Add(propValue);
                        property.SetValue(subObject, objValue);
                    }

                    else if (!property.PropertyType.IsClass || property.PropertyType == typeof(string))
                    {
                        var typeConverter = TypeDescriptor.GetConverter(property.PropertyType);
                        var propValue = typeConverter.ConvertFromString($"{values[i]}");

                        property.SetValue(subObject, propValue);
                    }

                    else if (property.PropertyType.IsClass && !property.PropertyType.IsArray)
                    {
                        if (objValue == null)
                        {
                            objValue = Activator.CreateInstance(property.PropertyType);
                        }
                        property.SetValue(subObject, objValue);

                        var resultObjectPropertiesCount = GetPropertiesCount(objValue, property.PropertyType);
                        var resultObject = GetSubObject(property, headers, values, i, i + resultObjectPropertiesCount);

                        property.SetValue(subObject, resultObject);
                    }
                }
            }

            return subObject;
        }
    }
}
