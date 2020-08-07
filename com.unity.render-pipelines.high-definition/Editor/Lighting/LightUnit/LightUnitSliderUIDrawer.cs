﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityEditor.Rendering.HighDefinition
{
    internal class LightUnitSliderUIDrawer
    {
        private class LightUnitLevels
        {
            public LightUnitLevels(string cautionTooltip)
            {
                // Load builtin caution icon.
                m_CautionContent = EditorGUIUtility.IconContent("console.warnicon.sml");
                m_CautionContent.tooltip = cautionTooltip;
            }

            public void AddLevel(Texture2D icon, string tooltip, Vector2 range)
            {
                LightUnitUILevel level;
                level.content = new GUIContent(icon, tooltip);
                level.range = range;
                m_Levels.Add(level);

                // Update the slider ranges.
                if (range.y > m_RangeMax)
                    m_RangeMax = range.y;
                else if (range.x < m_RangeMin)
                    m_RangeMin = range.x;
            }

            private void CurrentLevel(float value, out GUIContent level)
            {
                foreach (var l in m_Levels)
                {
                    if (value >= l.range.x && value <= l.range.y)
                    {
                        level = l.content;
                        return;
                    }
                }

                // If value out of range, indicate caution. (For now assume caution feedback is last)
                level = m_CautionContent;
            }

            public void Draw(Rect rect, SerializedProperty value)
            {
                // Fetch the rects
                GetRects(rect, out var sliderRect, out var iconRect);

                // Slider
                DoSlider(sliderRect, value, m_RangeMin, m_RangeMax);

                // Markers
                foreach (var l in m_Levels)
                {
                    var markerValue = l.range.y / m_RangeMax;
                    DoSliderMarker(sliderRect, markerValue, l.content.tooltip);
                }

                // Icon
                CurrentLevel(value.floatValue, out var level);
                DoIcon(iconRect, level);
            }

            private readonly GUIContent m_CautionContent;
            private readonly List<LightUnitUILevel> m_Levels = new List<LightUnitUILevel>();
            private float m_RangeMin = float.MaxValue;
            private float m_RangeMax = float.MinValue;
        }

        private static readonly Dictionary<LightUnit, LightUnitLevels> s_LightUnitLevelMap = new Dictionary<LightUnit, LightUnitLevels>();
        private static readonly GUIContent s_MarkerContent;

        static LightUnitSliderUIDrawer()
        {
            // Load light unit icons from editor resources
            var editorTextures = HDRenderPipeline.defaultAsset.renderPipelineEditorResources.textures;

            // TODO: Fill a table of light unit presets, containing their icon, tooltip, and range
            var luxPresets = new LightUnitLevels("Higher than Sunlight");
            luxPresets.AddLevel(editorTextures.lightUnitVeryBrightSun,   "Very Bright Sun",   new Vector2(80000, 120000));
            luxPresets.AddLevel(editorTextures.lightUnitOvercastSky,     "Overcast Sky",      new Vector2(10000, 80000));
            luxPresets.AddLevel(editorTextures.lightUnitSunriseOrSunset, "Sunrise or Sunset", new Vector2(1,     10000));
            luxPresets.AddLevel(editorTextures.lightUnitMoonLight,       "Moon Light",        new Vector2(0,     1));;
            s_LightUnitLevelMap.Add(LightUnit.Lux, luxPresets);

            // var lumenPresets = new LightUnitLevels("Very High Intensity Light");
            // lumenPresets.AddLevel(editorTextures.lightUnitExterior,   "Exterior",   new Vector2(3000, 40000));
            // lumenPresets.AddLevel(editorTextures.lightUnitInterior,   "Interior",   new Vector2(300,   3000));
            // lumenPresets.AddLevel(editorTextures.lightUnitDecorative, "Decorative", new Vector2(15,     300));
            // lumenPresets.AddLevel(editorTextures.lightUnitCandle,     "Candle",     new Vector2(0,      15));
            // s_LightUnitLevelMap.Add(LightUnit.Lumen, lumenPresets);

            s_MarkerContent = new GUIContent(string.Empty);
        }

        public void OnGUI(LightUnit unit, SerializedProperty value)
        {
            OnGUI(unit, value, EditorGUILayout.GetControlRect());
        }

        public void OnGUI(LightUnit unit, SerializedProperty value, Rect rect)
        {
            if (!s_LightUnitLevelMap.TryGetValue(unit, out var lightUnitLevels))
                return;

            // Disable indentation (breaks tooltips otherwise).
            var prevIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // Draw
            lightUnitLevels.Draw(rect, value);

            // Restore indentation
            EditorGUI.indentLevel = prevIndentLevel;

        }

        private static void GetRects(Rect baseRect, out Rect sliderRect, out Rect iconRect)
        {
            const int k_IconSeparator = 6;

            sliderRect = baseRect;
            sliderRect.width -= EditorGUIUtility.singleLineHeight + k_IconSeparator;

            iconRect = baseRect;
            iconRect.x += sliderRect.width + k_IconSeparator;
            iconRect.width = EditorGUIUtility.singleLineHeight;
        }

        private static void DoSlider(Rect rect, SerializedProperty value, float leftValue, float rightValue)
        {
            // TODO: Look into compiling a lambda to access internal slider function for logarithmic sliding.
            value.floatValue = GUI.HorizontalSlider(rect, value.floatValue, leftValue, rightValue, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
        }

        private static void DoSliderMarker(Rect rect, float x, string tooltip)
        {
            const float width  = 3f;
            const float height = 2f;

            var markerRect = rect;
            markerRect.width  = width;
            markerRect.height = height;

            // Vertically align with slider.
            markerRect.y += (EditorGUIUtility.singleLineHeight / 2f) - 1;

            // Horizontally place on slider.
            markerRect.x = rect.x + rect.width * x;

            // Clamp to the slider edges.
            float halfWidth = width * 0.5f;
            float min = rect.x + halfWidth;
            float max = (rect.x + rect.width) - halfWidth;
            markerRect.x = Mathf.Clamp(markerRect.x, min, max);

            // Center the marker on value.
            markerRect.x -= halfWidth;

            // Draw marker by manually drawing the rect, and an empty label with the tooltip.
            EditorGUI.DrawRect(markerRect, Color.white);

            // TODO: Consider enlarging this tooltip rect so that it's easier to discover?
            s_MarkerContent.tooltip = tooltip;
            EditorGUI.LabelField(markerRect, s_MarkerContent);
        }

        private static void DoIcon(Rect rect, GUIContent icon)
        {
            var oldColor = GUI.color;
            GUI.color = Color.clear;
            EditorGUI.DrawTextureTransparent(rect, icon.image);
            GUI.color = oldColor;

            EditorGUI.LabelField(rect, new GUIContent(string.Empty, icon.tooltip));
        }
    }
}
