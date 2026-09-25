using System.Collections.Generic;
using DnWModLoader.Logging;
using UnityEngine;

namespace DnWModLoader
{
    internal sealed class LogView
    {
        private const string EmptyText = "No logs at the current log level";
        private static readonly GUILayoutOption[] FillHeight = { GUILayout.ExpandHeight(true) };
        private static readonly GUILayoutOption[] FillWidth = { GUILayout.ExpandWidth(true) };

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Row> _shown = new List<Row>();
        private readonly List<LogLine> _incoming = new List<LogLine>();
        private readonly GUIContent _content = new GUIContent();
        private GUIStyle _style, _rowStyle;
        private long _seen;
        private LogLevel? _minimum;
        private float _measuredWidth;
        private float _areaWidth;
        private float _viewHeight;
        private float _height;
        private Vector2 _scroll;

        public void Draw(LogLevel minimum, GUIStyle style)
        {
            if (!ReferenceEquals(style, _style))
            {
                _style = style;
                _rowStyle = new GUIStyle(style) { padding = new RectOffset(style.padding.left, style.padding.right, 0, 0), margin = new RectOffset() };
                _measuredWidth = 0f;
            }

            var e = Event.current;
            if (e.type == EventType.Layout) Refresh(minimum);

            _scroll = GUILayout.BeginScrollView(_scroll, FillHeight);
            if (_shown.Count == 0) GUILayout.Label(EmptyText, _style);
            else
            {
                var area = GUILayoutUtility.GetRect(0f, _height, _style, FillWidth);
                if (e.type == EventType.Repaint)
                {
                    _areaWidth = area.width;
                    DrawRows(area);
                }
            }
            GUILayout.EndScrollView();
            if (e.type == EventType.Repaint) _viewHeight = GUILayoutUtility.GetLastRect().height;
        }

        private void Refresh(LogLevel minimum)
        {
            bool changed = false;
            _seen = Log.CopyRecent(_seen, _incoming);
            if (_incoming.Count > 0)
            {
                foreach (var line in _incoming) _rows.Add(new Row(line));
                _incoming.Clear();
                int dropped = _rows.Count - Log.RecentCapacity;
                if (dropped > 0) _rows.RemoveRange(0, dropped);
                changed = true;
            }
            if (_areaWidth != _measuredWidth)
            {
                _measuredWidth = _areaWidth;
                foreach (var row in _rows) row.Height = -1f;
                changed = true;
            }
            if (minimum != _minimum)
            {
                _minimum = minimum;
                changed = true;
            }
            if (!changed) return;

            _shown.Clear();
            _height = 0f;
            foreach (var row in _rows)
            {
                if (row.Line.Entry.Level < minimum) continue;
                if (row.Height < 0f) row.Height = Measure(row.Line.Text);
                row.Top = _height;
                _height += row.Height;
                _shown.Add(row);
            }
        }

        private float Measure(string text)
        {
            if (_measuredWidth <= 0f) return 0f;
            _content.text = text;
            return _rowStyle.CalcHeight(_content, _measuredWidth);
        }

        private void DrawRows(Rect area)
        {
            float origin = area.y + _style.padding.top;
            float top = _scroll.y - origin;
            float bottom = top + (_viewHeight > 0f ? _viewHeight : Screen.height);
            for (int i = FirstRowEndingBelow(top); i < _shown.Count; i++)
            {
                var row = _shown[i];
                if (row.Top > bottom) break;
                _content.text = row.Line.Text;
                GUI.Label(new Rect(area.x, origin + row.Top, area.width, row.Height), _content, _rowStyle);
            }
        }

        private int FirstRowEndingBelow(float y)
        {
            int low = 0, high = _shown.Count - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                var row = _shown[middle];
                if (row.Top + row.Height <= y) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private sealed class Row
        {
            public Row(LogLine line)
            {
                Line = line;
                Height = -1f;
            }

            public LogLine Line { get; }
            public float Height { get; set; }
            public float Top { get; set; }
        }
    }
}
