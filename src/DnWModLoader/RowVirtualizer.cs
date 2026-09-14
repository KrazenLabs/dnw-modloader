using System.Collections.Generic;
using UnityEngine;

namespace DnWModLoader
{
    // Prevents rendering of IMGUI rows outside the view
    internal sealed class RowVirtualizer
    {
        private const float Margin = 100f;   // Rows this close to the view are still drawn
        private const float DefaultHeight = 30f;

        private readonly Dictionary<string, Rect> _measured = new Dictionary<string, Rect>();
        private readonly Dictionary<string, Rect> _frozen = new Dictionary<string, Rect>();
        private readonly Dictionary<string, float> _typicalHeights = new Dictionary<string, float>();
        private GUIStyle _rowStyle;

        private Vector2 _scroll;
        private float _viewportHeight;
        private float _cursor;
        private bool _remeasureRequested;
        private bool _remeasuring;
        private int _drawn;
        private int _total;
        private float _contentHeight;

        // The row between BeginRow and EndRow
        private string _key;
        private string _kind;
        private bool _live;
        private float _height;

        public bool DrawAll { get; set; }

        public float ContentHeight { get; private set; }

        public int RowsDrawn { get; private set; }

        public int RowsTotal { get; private set; }

        public void RemeasureNextFrame()
        {
            _remeasureRequested = true;
        }

        // Called after BeginScrollView
        public void BeginPass(Vector2 scroll, float viewportHeight)
        {
            if (Event.current.type == EventType.Layout)
            {
                _frozen.Clear();
                foreach (var pair in _measured) _frozen[pair.Key] = pair.Value;
                if (_remeasureRequested) _remeasuring = true;
                _remeasureRequested = false;
            }
            _scroll = scroll;
            _viewportHeight = viewportHeight;
            _cursor = 0f;
            _drawn = 0;
            _total = 0;
            _contentHeight = 0f;
        }

        public bool BeginRow(string key, string kind, bool required = false)
        {
            _key = key;
            _kind = kind;
            float y;
            if (_frozen.TryGetValue(key, out var known))
            {
                y = known.y;
                _height = known.height;
            }
            else
            {
                y = _cursor;
                _height = _typicalHeights.TryGetValue(kind, out float typical) ? typical : DefaultHeight;
            }
            bool nearViewport = y + _height >= _scroll.y - Margin && y <= _scroll.y + _viewportHeight + Margin;
            _live = required || DrawAll || _remeasuring || nearViewport;
            _total++;
            if (_live) _drawn++;

            GUILayout.BeginVertical(RowStyle);
            GUILayout.Space(0f);
            return _live;
        }

        public void EndRow()
        {
            if (!_live) GUILayout.Space(_height);
            GUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint)
            {
                var rect = GUILayoutUtility.GetLastRect();
                if (_live) _typicalHeights[_kind] = rect.height;
                else if (_measured.TryGetValue(_key, out var previous)) rect.height = previous.height;   // a stand-in never measures itself
                _measured[_key] = rect;
                if (rect.yMax > _contentHeight) _contentHeight = rect.yMax;
            }
            _cursor = _frozen.TryGetValue(_key, out var frozen) ? frozen.yMax : _cursor + _height;
        }

        // Called after EndScrollView
        public void EndPass()
        {
            if (Event.current.type != EventType.Repaint) return;
            RowsDrawn = _drawn;
            RowsTotal = _total;
            ContentHeight = _contentHeight;
            _remeasuring = false;
        }

        private GUIStyle RowStyle
        {
            get
            {
                if (_rowStyle == null)
                    _rowStyle = new GUIStyle { margin = new RectOffset(), padding = new RectOffset(), stretchWidth = true, stretchHeight = false };
                return _rowStyle;
            }
        }
    }
}
