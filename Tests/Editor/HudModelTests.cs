using DreamTech.DevTools.Unity;
using NUnit.Framework;
using UnityEngine;

namespace DreamTech.DevTools.Tests
{
	public sealed class HudModelTests
	{
		[Test]
		public void Style_ConfirmCommandsWithoutAStyleReadAsDanger()
		{
			var plain = new DevCommand();
			var asks = new DevCommand { Confirm = true };
			var styled = new DevCommand { Confirm = true, Style = DevCommandStyle.Warning };
			Assert.AreEqual(DevCommandStyle.Default, plain.EffectiveStyle);
			Assert.AreEqual(DevCommandStyle.Danger, asks.EffectiveStyle);
			Assert.AreEqual(DevCommandStyle.Warning, styled.EffectiveStyle, "an explicit style wins");
		}

		[Test]
		public void With_SetsOnlyWhatItIsGiven()
		{
			var c = new DevCommand { Quick = true }.With(style: DevCommandStyle.Positive);
			Assert.IsTrue(c.Quick, "untouched");
			Assert.AreEqual(DevCommandStyle.Positive, c.Style);
			Assert.IsTrue(c.With(confirm: true).Confirm);
		}

		[Test]
		public void StandardLevelCommands_AreStyled()
		{
			using (DevTools.Install(new TestLevel()))
			{
				var r = DevTools.Registry;
				Assert.AreEqual(DevCommandStyle.Positive, r.Find("level.win").Style);
				Assert.AreEqual(DevCommandStyle.Danger, r.Find("level.lose").Style);
				Assert.AreEqual(DevCommandStyle.Warning, r.Find("level.restart").Style);
			}
		}

		sealed class TestLevel : ILevelPort
		{
			public int CurrentLevel => 1;
			public bool IsPlaying => true;
			public string StatusText => "";
			public void Win() { }
			public void Lose() { }
			public void Restart() { }
			public void JumpTo(int level) { }
		}

		[Test]
		public void CategoryIcon_AcceptsKnownNamesOnly()
		{
			Assert.IsTrue(DevToolsHud.SetCategoryIcon("Golden Race", "trophy"));
			Assert.IsTrue(DevToolsHud.SetCategoryIcon("Hearts", "Heart"), "case does not matter");
			Assert.IsTrue(DevToolsHud.SetCategoryIcon("Daily", "list"), "list is an alias");
			Assert.IsFalse(DevToolsHud.SetCategoryIcon("Golden Race", "rocket"));
			Assert.IsFalse(DevToolsHud.SetCategoryIcon("", "trophy"));
		}

		[Test]
		public void CornerTaps_CountOnlyQuickTapsInARow()
		{
			var counter = new CornerTapCounter();
			Assert.AreEqual(1, counter.Register(0f, 1.5f));
			Assert.AreEqual(2, counter.Register(1f, 1.5f));
			Assert.AreEqual(3, counter.Register(2.4f, 1.5f));
			Assert.AreEqual(1, counter.Register(5f, 1.5f), "a slow tap starts over");
		}

		[Test]
		public void CornerTaps_RegionIsTheTopLeftSquare()
		{
			// 1080x1920, 14 % of the short side = 151 px; screen origin is bottom-left
			Assert.IsTrue(CornerTapCounter.IsInCorner(new Vector2(20f, 1900f), 1080, 1920, 0.14f));
			Assert.IsFalse(CornerTapCounter.IsInCorner(new Vector2(20f, 100f), 1080, 1920, 0.14f), "bottom-left is not it");
			Assert.IsFalse(CornerTapCounter.IsInCorner(new Vector2(500f, 1900f), 1080, 1920, 0.14f), "top-middle is not it");
		}
	}
}
