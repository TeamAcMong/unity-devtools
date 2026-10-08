#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamTech.DevTools.Unity
{
	/// <summary>
	/// Vector icon drawn with Painter2D in the element's USS <c>color</c>. Drawing the glyphs (instead of printing characters)
	/// keeps them identical on every device: the default runtime font has no star / arrow glyphs.
	/// </summary>
	sealed class DevIcon : VisualElement
	{
		public enum Shape
		{
			Star,
			StarFilled,
			ChevronUp,
			ChevronDown,
			Close,
			Plus,
			Minus,
			Search,
			Check,
			Reload,
			ChevronLeft,
			ChevronRight,
			Expand,
		}

		Shape _shape;

		public DevIcon(Shape shape)
		{
			_shape = shape;
			pickingMode = PickingMode.Ignore;
			AddToClassList("dt-icon");
			generateVisualContent += Draw;
			RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
		}

		public Shape Kind
		{
			get => _shape;
			set
			{
				if (_shape == value) return;
				_shape = value;
				MarkDirtyRepaint();
			}
		}

		void Draw(MeshGenerationContext context)
		{
			Rect r = contentRect;
			if (r.width < 1f || r.height < 1f) return;
			var p = context.painter2D;
			Color color = resolvedStyle.color;
			float size = Mathf.Min(r.width, r.height);
			Vector2 c = r.center;
			float h = size * 0.5f;
			p.strokeColor = color;
			p.fillColor = color;
			p.lineWidth = Mathf.Max(1.6f, size * 0.11f);
			p.lineCap = LineCap.Round;
			p.lineJoin = LineJoin.Round;
			switch (_shape)
			{
				case Shape.Star:
				case Shape.StarFilled:
				{
					float outer = h * 0.98f, inner = outer * 0.45f;
					p.BeginPath();
					for (int i = 0; i < 10; i++)
					{
						float angle = -Mathf.PI * 0.5f + i * Mathf.PI / 5f;
						float radius = i % 2 == 0 ? outer : inner;
						var pt = new Vector2(c.x + Mathf.Cos(angle) * radius, c.y + 0.04f * size + Mathf.Sin(angle) * radius);
						if (i == 0) p.MoveTo(pt);
						else p.LineTo(pt);
					}
					p.ClosePath();
					if (_shape == Shape.StarFilled) p.Fill();
					else p.Stroke();
					break;
				}
				case Shape.ChevronUp:
				case Shape.ChevronDown:
				{
					float dy = _shape == Shape.ChevronUp ? -1f : 1f;
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.55f, c.y - dy * h * 0.22f));
					p.LineTo(new Vector2(c.x, c.y + dy * h * 0.33f));
					p.LineTo(new Vector2(c.x + h * 0.55f, c.y - dy * h * 0.22f));
					p.Stroke();
					break;
				}
				case Shape.Close:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.5f, c.y - h * 0.5f));
					p.LineTo(new Vector2(c.x + h * 0.5f, c.y + h * 0.5f));
					p.MoveTo(new Vector2(c.x + h * 0.5f, c.y - h * 0.5f));
					p.LineTo(new Vector2(c.x - h * 0.5f, c.y + h * 0.5f));
					p.Stroke();
					break;
				case Shape.Plus:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.55f, c.y));
					p.LineTo(new Vector2(c.x + h * 0.55f, c.y));
					p.MoveTo(new Vector2(c.x, c.y - h * 0.55f));
					p.LineTo(new Vector2(c.x, c.y + h * 0.55f));
					p.Stroke();
					break;
				case Shape.Minus:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.55f, c.y));
					p.LineTo(new Vector2(c.x + h * 0.55f, c.y));
					p.Stroke();
					break;
				case Shape.Check:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.6f, c.y + h * 0.02f));
					p.LineTo(new Vector2(c.x - h * 0.15f, c.y + h * 0.45f));
					p.LineTo(new Vector2(c.x + h * 0.62f, c.y - h * 0.45f));
					p.Stroke();
					break;
				case Shape.Reload:
				{
					// an open circle with an arrow head at its end
					// three quarters of a circle (gap at the top right) ending in an arrow head, like a refresh glyph
					float radius = h * 0.6f;
					p.BeginPath();
					p.Arc(c, radius, Angle.Degrees(-20f), Angle.Degrees(250f));
					p.Stroke();
					float endAngle = -20f * Mathf.Deg2Rad;
					var end = c + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * radius;
					p.BeginPath();
					p.MoveTo(end + new Vector2(-h * 0.5f, -h * 0.08f));
					p.LineTo(end + new Vector2(h * 0.04f, -h * 0.02f));
					p.LineTo(end + new Vector2(h * 0.12f, -h * 0.56f));
					p.Stroke();
					break;
				}
				case Shape.ChevronLeft:
				case Shape.ChevronRight:
				{
					float dx = _shape == Shape.ChevronLeft ? -1f : 1f;
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - dx * h * 0.22f, c.y - h * 0.55f));
					p.LineTo(new Vector2(c.x + dx * h * 0.33f, c.y));
					p.LineTo(new Vector2(c.x - dx * h * 0.22f, c.y + h * 0.55f));
					p.Stroke();
					break;
				}
				case Shape.Expand:
				{
					// two corner brackets pointing outwards (top-right, bottom-left)
					float a = h * 0.62f, b = h * 0.12f;
					p.BeginPath();
					p.MoveTo(new Vector2(c.x + b, c.y - a));
					p.LineTo(new Vector2(c.x + a, c.y - a));
					p.LineTo(new Vector2(c.x + a, c.y - b));
					p.MoveTo(new Vector2(c.x - b, c.y + a));
					p.LineTo(new Vector2(c.x - a, c.y + a));
					p.LineTo(new Vector2(c.x - a, c.y + b));
					p.MoveTo(new Vector2(c.x + a, c.y - a));
					p.LineTo(new Vector2(c.x + h * 0.15f, c.y - h * 0.15f));
					p.MoveTo(new Vector2(c.x - a, c.y + a));
					p.LineTo(new Vector2(c.x - h * 0.15f, c.y + h * 0.15f));
					p.Stroke();
					break;
				}
				case Shape.Search:
				{
					var lens = new Vector2(c.x - h * 0.12f, c.y - h * 0.12f);
					float radius = h * 0.55f;
					p.BeginPath();
					p.Arc(lens, radius, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					p.BeginPath();
					p.MoveTo(lens + new Vector2(radius, radius) * 0.72f);
					p.LineTo(new Vector2(c.x + h * 0.78f, c.y + h * 0.78f));
					p.Stroke();
					break;
				}
			}
		}
	}
}
#endif
