using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Custom VisualElement that draws edge lines between nodes and traveling runner dots
    /// on the strategic map using Painter2D (generateVisualContent).
    /// </summary>
    public class StrategicMapEdgeDrawer : VisualElement
    {
        public struct EdgeData
        {
            public Vector2 Start;
            public Vector2 End;
        }

        public struct TravelingRunnerDot
        {
            public Vector2 Position;
            public string RunnerId;
            public bool IsSelected;
            public bool IsLeaving;
        }

        private readonly List<EdgeData> _edges = new();
        private readonly List<TravelingRunnerDot> _dots = new();

        /// <summary>
        /// Fired when a traveling runner dot is clicked. Passes the runner ID.
        /// </summary>
        public event Action<string> OnRunnerDotClicked;

        private const float EdgeLineWidth = 2f;
        private const float DotRadius = 10f;
        private const float ClickHitRadius = 18f;
        private const float SelectionRingPadding = 3f;

        private static readonly Color EdgeColor = new(0.4f, 0.4f, 0.55f, 0.4f);
        private static readonly Color DotColor = new(0.3f, 0.8f, 0.3f, 0.9f);
        private static readonly Color SelectionRingColor = new(0.86f, 0.7f, 0.23f, 0.85f);

        public StrategicMapEdgeDrawer()
        {
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        public void SetData(List<EdgeData> edges, List<TravelingRunnerDot> dots)
        {
            _edges.Clear();
            _edges.AddRange(edges);
            _dots.Clear();
            _dots.AddRange(dots);
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Returns the runner ID of the dot nearest to the given local position,
        /// or null if none is within click range.
        /// </summary>
        public string GetDotAtPosition(Vector2 localPos)
        {
            string closest = null;
            float closestDist = ClickHitRadius;

            for (int i = 0; i < _dots.Count; i++)
            {
                float dist = Vector2.Distance(localPos, _dots[i].Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = _dots[i].RunnerId;
                }
            }

            return closest;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            var localPos = evt.localPosition;
            string runnerId = GetDotAtPosition(new Vector2(localPos.x, localPos.y));
            if (runnerId != null)
            {
                evt.StopPropagation();
                OnRunnerDotClicked?.Invoke(runnerId);
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;

            // Draw edge lines
            painter.strokeColor = EdgeColor;
            painter.lineWidth = EdgeLineWidth;
            painter.lineCap = LineCap.Round;

            for (int i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                painter.BeginPath();
                painter.MoveTo(edge.Start);
                painter.LineTo(edge.End);
                painter.Stroke();
            }

            // Draw traveling runner dots (all green, gold ring when selected)
            for (int i = 0; i < _dots.Count; i++)
            {
                var dot = _dots[i];

                if (dot.IsSelected)
                {
                    painter.fillColor = SelectionRingColor;
                    painter.BeginPath();
                    painter.Arc(dot.Position, DotRadius + SelectionRingPadding, 0f, 360f);
                    painter.Fill();
                }

                painter.fillColor = DotColor;
                painter.BeginPath();
                painter.Arc(dot.Position, DotRadius, 0f, 360f);
                painter.Fill();
            }
        }
    }
}
