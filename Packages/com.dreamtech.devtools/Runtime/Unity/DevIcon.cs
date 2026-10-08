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
			Sliders,
			Coin,
			Flag,
			Play,
			Clock,
			Gear,
			ListLines,
			Eye,
			Info,
			Terminal,
			Database,
			Image,
			Trophy,
			Heart,
			Gift,
			User,
			Cart,
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
				case Shape.Sliders:
				{
					// three tracks with their knobs: reads as "settings / tweak", the DEV ball's own icon
					float[] rows = { -0.5f, 0f, 0.5f }, knobs = { -0.28f, 0.34f, -0.02f };
					for (int i = 0; i < 3; i++)
					{
						float y = c.y + rows[i] * h;
						p.BeginPath();
						p.MoveTo(new Vector2(c.x - h * 0.7f, y));
						p.LineTo(new Vector2(c.x + h * 0.7f, y));
						p.Stroke();
						p.BeginPath();
						p.Arc(new Vector2(c.x + knobs[i] * h, y), h * 0.19f, Angle.Degrees(0f), Angle.Degrees(360f));
						p.Fill();
					}
					break;
				}
				case Shape.Coin:
					p.BeginPath();
					p.Arc(c, h * 0.66f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					p.BeginPath();
					p.Arc(c, h * 0.3f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					break;
				case Shape.Flag:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.5f, c.y + h * 0.75f));
					p.LineTo(new Vector2(c.x - h * 0.5f, c.y - h * 0.72f));
					p.Stroke();
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.5f, c.y - h * 0.72f));
					p.LineTo(new Vector2(c.x + h * 0.62f, c.y - h * 0.44f));
					p.LineTo(new Vector2(c.x - h * 0.5f, c.y - h * 0.12f));
					p.ClosePath();
					p.Fill();
					break;
				case Shape.Play:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.38f, c.y - h * 0.58f));
					p.LineTo(new Vector2(c.x + h * 0.6f, c.y));
					p.LineTo(new Vector2(c.x - h * 0.38f, c.y + h * 0.58f));
					p.ClosePath();
					p.Fill();
					break;
				case Shape.Clock:
					p.BeginPath();
					p.Arc(c, h * 0.68f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					p.BeginPath();
					p.MoveTo(new Vector2(c.x, c.y - h * 0.4f));
					p.LineTo(c);
					p.LineTo(new Vector2(c.x + h * 0.3f, c.y + h * 0.12f));
					p.Stroke();
					break;
				case Shape.Gear:
				{
					p.BeginPath();
					p.Arc(c, h * 0.26f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					p.BeginPath();
					p.Arc(c, h * 0.52f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					for (int i = 0; i < 8; i++)
					{
						float angle = i * Mathf.PI / 4f;
						var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
						p.BeginPath();
						p.MoveTo(c + direction * h * 0.52f);
						p.LineTo(c + direction * h * 0.78f);
						p.Stroke();
					}
					break;
				}
				case Shape.ListLines:
					for (int i = -1; i <= 1; i++)
					{
						float y = c.y + i * h * 0.48f;
						p.BeginPath();
						p.Arc(new Vector2(c.x - h * 0.6f, y), h * 0.1f, Angle.Degrees(0f), Angle.Degrees(360f));
						p.Fill();
						p.BeginPath();
						p.MoveTo(new Vector2(c.x - h * 0.28f, y));
						p.LineTo(new Vector2(c.x + h * 0.7f, y));
						p.Stroke();
					}
					break;
				case Shape.Eye:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.8f, c.y));
					p.BezierCurveTo(new Vector2(c.x - h * 0.35f, c.y - h * 0.7f), new Vector2(c.x + h * 0.35f, c.y - h * 0.7f), new Vector2(c.x + h * 0.8f, c.y));
					p.BezierCurveTo(new Vector2(c.x + h * 0.35f, c.y + h * 0.7f), new Vector2(c.x - h * 0.35f, c.y + h * 0.7f), new Vector2(c.x - h * 0.8f, c.y));
					p.Stroke();
					p.BeginPath();
					p.Arc(c, h * 0.22f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Fill();
					break;
				case Shape.Info:
					p.BeginPath();
					p.Arc(c, h * 0.7f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					p.BeginPath();
					p.Arc(new Vector2(c.x, c.y - h * 0.33f), h * 0.09f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Fill();
					p.BeginPath();
					p.MoveTo(new Vector2(c.x, c.y - h * 0.06f));
					p.LineTo(new Vector2(c.x, c.y + h * 0.38f));
					p.Stroke();
					break;
				case Shape.Terminal:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.62f, c.y - h * 0.42f));
					p.LineTo(new Vector2(c.x - h * 0.16f, c.y));
					p.LineTo(new Vector2(c.x - h * 0.62f, c.y + h * 0.42f));
					p.Stroke();
					p.BeginPath();
					p.MoveTo(new Vector2(c.x + h * 0.05f, c.y + h * 0.46f));
					p.LineTo(new Vector2(c.x + h * 0.66f, c.y + h * 0.46f));
					p.Stroke();
					break;
				case Shape.Database:
					// a two-unit server stack
					for (int i = 0; i < 2; i++)
					{
						float top = c.y - h * 0.66f + i * h * 0.7f;
						p.BeginPath();
						p.MoveTo(new Vector2(c.x - h * 0.62f, top));
						p.LineTo(new Vector2(c.x + h * 0.62f, top));
						p.LineTo(new Vector2(c.x + h * 0.62f, top + h * 0.58f));
						p.LineTo(new Vector2(c.x - h * 0.62f, top + h * 0.58f));
						p.ClosePath();
						p.Stroke();
						p.BeginPath();
						p.Arc(new Vector2(c.x + h * 0.32f, top + h * 0.29f), h * 0.08f, Angle.Degrees(0f), Angle.Degrees(360f));
						p.Fill();
					}
					break;
				case Shape.Image:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.72f, c.y - h * 0.55f));
					p.LineTo(new Vector2(c.x + h * 0.72f, c.y - h * 0.55f));
					p.LineTo(new Vector2(c.x + h * 0.72f, c.y + h * 0.55f));
					p.LineTo(new Vector2(c.x - h * 0.72f, c.y + h * 0.55f));
					p.ClosePath();
					p.Stroke();
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.55f, c.y + h * 0.4f));
					p.LineTo(new Vector2(c.x - h * 0.12f, c.y - h * 0.05f));
					p.LineTo(new Vector2(c.x + h * 0.18f, c.y + h * 0.25f));
					p.LineTo(new Vector2(c.x + h * 0.36f, c.y + h * 0.08f));
					p.LineTo(new Vector2(c.x + h * 0.56f, c.y + h * 0.4f));
					p.Stroke();
					p.BeginPath();
					p.Arc(new Vector2(c.x + h * 0.32f, c.y - h * 0.22f), h * 0.11f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Fill();
					break;
				case Shape.Trophy:
					// cup with handles, stem and base
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.45f, c.y - h * 0.62f));
					p.LineTo(new Vector2(c.x + h * 0.45f, c.y - h * 0.62f));
					p.BezierCurveTo(new Vector2(c.x + h * 0.45f, c.y + h * 0.05f), new Vector2(c.x + h * 0.2f, c.y + h * 0.15f), new Vector2(c.x, c.y + h * 0.15f));
					p.BezierCurveTo(new Vector2(c.x - h * 0.2f, c.y + h * 0.15f), new Vector2(c.x - h * 0.45f, c.y + h * 0.05f), new Vector2(c.x - h * 0.45f, c.y - h * 0.62f));
					p.Stroke();
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.45f, c.y - h * 0.45f));
					p.BezierCurveTo(new Vector2(c.x - h * 0.85f, c.y - h * 0.45f), new Vector2(c.x - h * 0.8f, c.y - h * 0.05f), new Vector2(c.x - h * 0.38f, c.y - h * 0.08f));
					p.MoveTo(new Vector2(c.x + h * 0.45f, c.y - h * 0.45f));
					p.BezierCurveTo(new Vector2(c.x + h * 0.85f, c.y - h * 0.45f), new Vector2(c.x + h * 0.8f, c.y - h * 0.05f), new Vector2(c.x + h * 0.38f, c.y - h * 0.08f));
					p.MoveTo(new Vector2(c.x, c.y + h * 0.15f));
					p.LineTo(new Vector2(c.x, c.y + h * 0.5f));
					p.MoveTo(new Vector2(c.x - h * 0.38f, c.y + h * 0.66f));
					p.LineTo(new Vector2(c.x + h * 0.38f, c.y + h * 0.66f));
					p.Stroke();
					break;
				case Shape.Heart:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x, c.y + h * 0.68f));
					p.BezierCurveTo(new Vector2(c.x - h * 0.95f, c.y + h * 0.05f), new Vector2(c.x - h * 0.6f, c.y - h * 0.85f), new Vector2(c.x, c.y - h * 0.32f));
					p.BezierCurveTo(new Vector2(c.x + h * 0.6f, c.y - h * 0.85f), new Vector2(c.x + h * 0.95f, c.y + h * 0.05f), new Vector2(c.x, c.y + h * 0.68f));
					p.ClosePath();
					p.Fill();
					break;
				case Shape.Gift:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.62f, c.y - h * 0.2f));
					p.LineTo(new Vector2(c.x + h * 0.62f, c.y - h * 0.2f));
					p.LineTo(new Vector2(c.x + h * 0.62f, c.y + h * 0.66f));
					p.LineTo(new Vector2(c.x - h * 0.62f, c.y + h * 0.66f));
					p.ClosePath();
					p.MoveTo(new Vector2(c.x, c.y - h * 0.2f));
					p.LineTo(new Vector2(c.x, c.y + h * 0.66f));
					p.MoveTo(new Vector2(c.x, c.y - h * 0.2f));
					p.BezierCurveTo(new Vector2(c.x - h * 0.2f, c.y - h * 0.75f), new Vector2(c.x - h * 0.7f, c.y - h * 0.55f), new Vector2(c.x, c.y - h * 0.2f));
					p.MoveTo(new Vector2(c.x, c.y - h * 0.2f));
					p.BezierCurveTo(new Vector2(c.x + h * 0.2f, c.y - h * 0.75f), new Vector2(c.x + h * 0.7f, c.y - h * 0.55f), new Vector2(c.x, c.y - h * 0.2f));
					p.Stroke();
					break;
				case Shape.User:
					p.BeginPath();
					p.Arc(new Vector2(c.x, c.y - h * 0.3f), h * 0.3f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Stroke();
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.66f, c.y + h * 0.7f));
					p.BezierCurveTo(new Vector2(c.x - h * 0.6f, c.y + h * 0.1f), new Vector2(c.x + h * 0.6f, c.y + h * 0.1f), new Vector2(c.x + h * 0.66f, c.y + h * 0.7f));
					p.Stroke();
					break;
				case Shape.Cart:
					p.BeginPath();
					p.MoveTo(new Vector2(c.x - h * 0.8f, c.y - h * 0.6f));
					p.LineTo(new Vector2(c.x - h * 0.55f, c.y - h * 0.6f));
					p.LineTo(new Vector2(c.x - h * 0.35f, c.y + h * 0.25f));
					p.LineTo(new Vector2(c.x + h * 0.55f, c.y + h * 0.25f));
					p.LineTo(new Vector2(c.x + h * 0.72f, c.y - h * 0.35f));
					p.LineTo(new Vector2(c.x - h * 0.48f, c.y - h * 0.35f));
					p.Stroke();
					p.BeginPath();
					p.Arc(new Vector2(c.x - h * 0.25f, c.y + h * 0.58f), h * 0.1f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Fill();
					p.BeginPath();
					p.Arc(new Vector2(c.x + h * 0.45f, c.y + h * 0.58f), h * 0.1f, Angle.Degrees(0f), Angle.Degrees(360f));
					p.Fill();
					break;
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
