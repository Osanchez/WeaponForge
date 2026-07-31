using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WeaponForge
{
    // Generic JSON -> object field mapper. Walks a JObject and copies
    // each property onto the matching public field (case-insensitive)
    // of the target, recursing into nested objects and lists. Unity
    // object references (prefabs, Resources, Sprites, ...) are looked
    // up by asset name, so JSON can say "damageType": "Resource White"
    // or "projectilePrefab": "Projectile Dart".
    public static class JsonFieldMapper
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("WeaponForge");

        // Applies every property in json onto target. Returns the
        // (possibly re-boxed) target so struct targets keep changes.
        public static object Apply(
            object target,
            JObject json,
            string path)
        {
            if (target == null || json == null)
                return target;

            Type type = target.GetType();

            foreach (JProperty property in json.Properties())
            {
                FieldInfo field =
                    FindField(type, property.Name);

                if (field == null)
                {
                    Log.LogWarning(
                        path + "." + property.Name +
                        " does not match any field on " +
                        type.Name + " - skipped");
                    continue;
                }

                try
                {
                    object converted;

                    if (TryConvert(
                        property.Value,
                        field.FieldType,
                        field.GetValue(target),
                        path + "." + property.Name,
                        out converted))
                    {
                        field.SetValue(target, converted);
                    }
                }
                catch (Exception e)
                {
                    Log.LogWarning(
                        path + "." + property.Name + ": " +
                        e.Message);
                }
            }

            return target;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            FieldInfo[] fields =
                type.GetFields(
                    BindingFlags.Public |
                    BindingFlags.Instance);

            foreach (FieldInfo field in fields)
            {
                if (string.Equals(
                    field.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }

            return null;
        }

        private static bool TryConvert(
            JToken token,
            Type fieldType,
            object currentValue,
            string path,
            out object result)
        {
            result = null;

            // Unity object references: look up by asset name.
            if (typeof(UnityEngine.Object)
                .IsAssignableFrom(fieldType))
            {
                if (token.Type == JTokenType.Null)
                {
                    result = null;
                    return true;
                }

                string assetName = token.ToString();

                // ColorAssets also accept raw hex colors, e.g.
                // "#7fd4ff" — a new ColorAsset is created on the fly.
                if (fieldType == typeof(ColorAsset) &&
                    assetName.StartsWith("#"))
                {
                    Color parsed;

                    if (!ColorUtility.TryParseHtmlString(
                        assetName,
                        out parsed))
                    {
                        Log.LogWarning(
                            path + ": '" + assetName +
                            "' is not a valid hex color");
                        return false;
                    }

                    var colorAsset =
                        ScriptableObject
                            .CreateInstance<ColorAsset>();

                    colorAsset.name =
                        "Forge Color " + assetName;

                    colorAsset.color = parsed;

                    result = colorAsset;
                    return true;
                }

                UnityEngine.Object found =
                    FindAsset(fieldType, assetName);

                if (found == null)
                {
                    Log.LogWarning(
                        path + ": no " + fieldType.Name +
                        " asset named '" + assetName +
                        "' was found");
                    return false;
                }

                result = found;
                return true;
            }

            if (fieldType.IsEnum)
            {
                result = Enum.Parse(
                    fieldType,
                    token.ToString(),
                    true);
                return true;
            }

            // AnimationCurve as [[time, value], ...] - used by things like
            // homingData.turbulenceDistanceCurve, where the curve maps
            // distance-to-target onto how strong an effect is.
            if (fieldType == typeof(AnimationCurve))
            {
                if (token.Type != JTokenType.Array)
                    return false;

                var curve = new AnimationCurve();
                foreach (JToken point in token)
                {
                    if (point.Type != JTokenType.Array)
                        continue;
                    var pair = point.ToArray();
                    if (pair.Length >= 2)
                        curve.AddKey((float)pair[0], (float)pair[1]);
                }

                if (curve.length == 0)
                    return false;

                result = curve;
                return true;
            }

            if (fieldType == typeof(LayerMask))
            {
                if (token.Type == JTokenType.Integer)
                {
                    result = (LayerMask)(int)token;
                    return true;
                }

                if (token.Type == JTokenType.String)
                {
                    result = (LayerMask)LayerMask.GetMask(
                        token.ToString());
                    return true;
                }

                if (token.Type == JTokenType.Array)
                {
                    result = (LayerMask)LayerMask.GetMask(
                        token.Select(t => t.ToString())
                            .ToArray());
                    return true;
                }

                return false;
            }

            if (fieldType == typeof(float))
            {
                result = (float)token;
                return true;
            }

            if (fieldType == typeof(int))
            {
                result = (int)token;
                return true;
            }

            if (fieldType == typeof(bool))
            {
                result = (bool)token;
                return true;
            }

            if (fieldType == typeof(string))
            {
                result = token.ToString();
                return true;
            }

            // A single asset NAME where a list of assets is expected, e.g.
            // "convertableCells": "CellType_Mud" - treat it as a one-item
            // list so you don't have to write [...] for the common case.
            if (token.Type == JTokenType.String &&
                fieldType.IsGenericType &&
                fieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                typeof(UnityEngine.Object).IsAssignableFrom(
                    fieldType.GetGenericArguments()[0]))
            {
                Type single = fieldType.GetGenericArguments()[0];
                UnityEngine.Object one = FindAsset(single, token.ToString());

                if (one == null)
                {
                    Log.LogWarning(
                        path + ": no " + single.Name + " asset named '" +
                        token + "' was found");
                    return false;
                }

                IList oneList = (IList)Activator.CreateInstance(fieldType);
                oneList.Add(one);
                result = oneList;
                return true;
            }

            // Lists of structs/classes, e.g. Explosion.damages.
            if (token.Type == JTokenType.Array &&
                fieldType.IsGenericType &&
                fieldType.GetGenericTypeDefinition() ==
                    typeof(List<>))
            {
                Type elementType =
                    fieldType.GetGenericArguments()[0];

                IList list =
                    (IList)Activator.CreateInstance(fieldType);

                // A list of ASSETS (e.g. cellConvertData.convertableCells,
                // a List<CellType>) is written as a list of asset NAMES -
                // those must be looked up, never constructed.
                if (typeof(UnityEngine.Object).IsAssignableFrom(elementType))
                {
                    foreach (JToken element in (JArray)token)
                    {
                        string assetName = element.ToString();
                        UnityEngine.Object asset =
                            FindAsset(elementType, assetName);

                        if (asset == null)
                        {
                            Log.LogWarning(
                                path + ": no " + elementType.Name +
                                " asset named '" + assetName + "' was found");
                            continue;
                        }

                        list.Add(asset);
                    }

                    result = list;
                    return true;
                }

                int index = 0;

                foreach (JToken element in (JArray)token)
                {
                    object item =
                        Activator.CreateInstance(elementType);

                    JObject elementObject = element as JObject;

                    if (elementObject != null)
                    {
                        item = Apply(
                            item,
                            elementObject,
                            path + "[" + index + "]");
                    }

                    list.Add(item);
                    index++;
                }

                result = list;
                return true;
            }

            // Nested serializable struct/class: recurse into the
            // field's current value so unspecified members keep
            // their template values.
            JObject nested = token as JObject;

            if (nested != null)
            {
                object value = currentValue;

                if (value == null && !fieldType.IsValueType)
                {
                    value =
                        Activator.CreateInstance(fieldType);
                }

                result = Apply(value, nested, path);
                return true;
            }

            Log.LogWarning(
                path + ": unsupported field type " +
                fieldType.Name);
            return false;
        }

        public static UnityEngine.Object FindAsset(
            Type type,
            string name)
        {
            UnityEngine.Object[] all =
                Resources.FindObjectsOfTypeAll(type);

            foreach (UnityEngine.Object asset in all)
            {
                if (string.Equals(
                    asset.name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return asset;
                }
            }

            // Convenience: allow "White" for "Resource White".
            if (type == typeof(Resource))
            {
                foreach (UnityEngine.Object asset in all)
                {
                    if (string.Equals(
                        asset.name,
                        "Resource " + name,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return asset;
                    }
                }
            }

            return null;
        }
    }
}
