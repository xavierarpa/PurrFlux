/*
Copyright (c) 2026 Xavier Arpa López Thomas Peter ('xavierarpa')

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UniFlux.Core;
using UniFlux.Editor;
using UniFlux;
using PurrNet.Editor;

namespace PurrFlux.Editor
{
    [CustomEditor(typeof(MonoPurrFlux), true)]
    public class MonoPurrFluxEditor : NetworkIdentityInspector
    {
        private struct MethodEntry
        {
            public MethodInfo method;
            #pragma warning disable CS0618
            public FluxAttribute attribute;
            #pragma warning restore CS0618
            public EntryKind kind;
        }

        private enum EntryKind
        {
            MethodFlux,
            StateFlux,
            MethodPurrFlux
        }

        private List<MethodEntry> entries;
        private Dictionary<MethodInfo, object> inputValues;
        private Dictionary<MethodInfo, object> outputValues;

        private static bool ShowMethods
        {
            get => PlayerPrefs.GetInt("__PurrFlux.PurrFluxEditor.ShowBox", default) == default;
            set => PlayerPrefs.SetInt("__PurrFlux.PurrFluxEditor.ShowBox", value ? default : 1);
        }

        protected new void OnEnable()
        {
            base.OnEnable();
            entries = new List<MethodEntry>();
            var type = target.GetType();
            var methods = type.GetMethods((BindingFlags)(-1));

            #pragma warning disable CS0618

            // Phase 1: concrete class methods
            foreach (var m in methods)
            {
                var attr = Attribute.GetCustomAttributes(m).FirstOrDefault(a => a is FluxAttribute) as FluxAttribute;
                if (attr != null)
                {
                    entries.Add(new MethodEntry
                    {
                        method = m,
                        attribute = attr,
                        kind = ClassifyAttribute(attr)
                    });
                }
            }

            // Phase 2: interface methods
            var interfaces = type.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                var map = type.GetInterfaceMap(interfaces[i]);
                for (int j = 0; j < map.InterfaceMethods.Length; j++)
                {
                    var ifaceMethod = map.InterfaceMethods[j];
                    var attr = Attribute.GetCustomAttributes(ifaceMethod).FirstOrDefault(a => a is FluxAttribute) as FluxAttribute;
                    if (attr != null)
                    {
                        var implMethod = map.TargetMethods[j];
                        if (!entries.Exists(e => e.method == implMethod))
                        {
                            entries.Add(new MethodEntry
                            {
                                method = implMethod,
                                attribute = attr,
                                kind = ClassifyAttribute(attr)
                            });
                        }
                    }
                }
            }

            #pragma warning restore CS0618

            inputValues = new Dictionary<MethodInfo, object>();
            outputValues = new Dictionary<MethodInfo, object>();
            foreach (var entry in entries)
            {
                inputValues[entry.method] = null;
                outputValues[entry.method] = null;
            }
        }

        #pragma warning disable CS0618
        private static EntryKind ClassifyAttribute(FluxAttribute attr)
        {
            if (attr is MethodPurrFluxAttribute)
            {
                return EntryKind.MethodPurrFlux;
            }

            if (attr is StateFluxAttribute)
            {
                return EntryKind.StateFlux;
            }

            return EntryKind.MethodFlux;
        }
        #pragma warning restore CS0618

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (entries == null || entries.Count == 0)
            {
                return;
            }

            GUILayout.Space(10);

            if (GUILayout.Button(ShowMethods
                ? $"Close PurrFlux Tool ({entries.Count})"
                : $"Open PurrFlux Tool ({entries.Count})"))
            {
                ShowMethods = !ShowMethods;
            }

            if (ShowMethods)
            {
                DrawEntries();
            }
        }

        private void DrawEntries()
        {
            GUILayout.Space(10);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                fontSize = 12
            };

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                richText = true
            };

            // Group by kind for clarity
            DrawGroup(EntryKind.MethodFlux, "MethodFlux", "#87CEEB", titleStyle, buttonStyle);
            DrawGroup(EntryKind.StateFlux, "StateFlux", "#FFD700", titleStyle, buttonStyle);
            DrawGroup(EntryKind.MethodPurrFlux, "MethodPurrFlux", "#FF6EC7", titleStyle, buttonStyle);
        }

        private void DrawGroup(EntryKind kind, string groupLabel, string groupColor,
            GUIStyle titleStyle, GUIStyle buttonStyle)
        {
            var group = entries.FindAll(e => e.kind == kind);
            if (group.Count == 0)
            {
                return;
            }

            GUILayout.Space(6);
            EditorGUILayout.LabelField($"<color={groupColor}><b>{groupLabel}</b></color>  ({group.Count})", titleStyle);
            GUILayout.Space(2);

            foreach (var entry in group)
            {
                DrawEntry(entry, groupColor, titleStyle, buttonStyle);
            }
        }

        private void DrawEntry(MethodEntry entry, string color,
            GUIStyle titleStyle, GUIStyle buttonStyle)
        {
            var method = entry.method;
            var attr = entry.attribute;
            var parameters = method.GetParameters();
            bool hasParams = parameters.Length > 0;
            bool hasReturn = method.ReturnType != typeof(void);
            bool isStatic = method.IsStatic;
            string keyColor = isStatic ? "yellow" : color;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Icon prefix for PurrFlux attributes
            string prefix = "";
            if (entry.kind == EntryKind.MethodPurrFlux)
            {
                prefix = "\u2601 ";
            }

            // Key
            GUILayout.Label($"{prefix}<color={keyColor}>{attr.key}</color>", titleStyle);

            // Method signature
            string kindTag = entry.kind.ToString();
            GUILayout.Label($"[{kindTag}] {method}", EditorStyles.whiteMiniLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (hasParams)
            {
                GUI.enabled = Application.isPlaying;
                inputValues[method] = GUILayouts.SuperField(
                    $"Input: {parameters[0].ParameterType.Name}",
                    parameters[0].ParameterType,
                    inputValues[method]);
                GUI.enabled = true;
            }

            GUI.enabled = Application.isPlaying;

            if (hasParams && hasReturn)
            {
                if (GUILayout.Button("Invoke!", buttonStyle))
                {
                    outputValues[method] = method.Invoke(target, new[] { inputValues[method] });
                }
            }
            else if (hasParams)
            {
                if (GUILayout.Button("Invoke!", buttonStyle))
                {
                    method.Invoke(target, new[] { inputValues[method] });
                }
            }
            else if (hasReturn)
            {
                if (GUILayout.Button("Invoke!", buttonStyle))
                {
                    outputValues[method] = method.Invoke(target, Array.Empty<object>());
                }
            }
            else
            {
                if (GUILayout.Button("Invoke!", buttonStyle))
                {
                    method.Invoke(target, Array.Empty<object>());
                }
            }

            GUI.enabled = true;

            if (hasReturn)
            {
                GUI.enabled = false;
                GUILayouts.SuperField(
                    $"Output: {method.ReturnType.Name}",
                    method.ReturnType,
                    outputValues[method]);
                GUI.enabled = true;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }
    }
}
