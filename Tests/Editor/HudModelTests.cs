using System.Collections.Generic;
using System.Linq;
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

		static DevToolsHost.LogRecord Entry(int id, LogType type, string message) =>
			new DevToolsHost.LogRecord { Id = id, Type = type, Message = message, Stack = "" };

		static readonly DevToolsHost.LogRecord[] SampleLogs =
		{
			Entry(1, LogType.Log, "boot"),
			Entry(2, LogType.Warning, "slow frame"),
			Entry(3, LogType.Error, "save failed"),
			Entry(4, LogType.Log, "boot"),
			Entry(5, LogType.Exception, "NullReferenceException"),
			Entry(6, LogType.Log, "Level 7 loaded"),
		};

		static int[] Ids(List<DevLogFilter.Row> rows) => rows.Select(r => SampleLogs[r.Index].Id).ToArray();

		[Test]
		public void LogFilter_NewestFirstAndByKind()
		{
			var rows = new List<DevLogFilter.Row>();
			DevLogFilter.Build(SampleLogs, DevLogKinds.All, "", false, 100, rows);
			CollectionAssert.AreEqual(new[] { 6, 5, 4, 3, 2, 1 }, Ids(rows));
			DevLogFilter.Build(SampleLogs, DevLogKinds.Error, "", false, 100, rows);
			CollectionAssert.AreEqual(new[] { 5, 3 }, Ids(rows), "exceptions count as errors");
			DevLogFilter.Build(SampleLogs, DevLogKinds.Warning | DevLogKinds.Log, "", false, 2, rows);
			CollectionAssert.AreEqual(new[] { 6, 4 }, Ids(rows), "capped at maxRows, newest kept");
		}

		[Test]
		public void LogFilter_SearchIgnoresCase()
		{
			var rows = new List<DevLogFilter.Row>();
			DevLogFilter.Build(SampleLogs, DevLogKinds.All, "LEVEL", false, 100, rows);
			CollectionAssert.AreEqual(new[] { 6 }, Ids(rows));
		}

		[Test]
		public void LogFilter_CollapseCountsRepeatsAtTheNewest()
		{
			var rows = new List<DevLogFilter.Row>();
			DevLogFilter.Build(SampleLogs, DevLogKinds.All, "", true, 100, rows);
			CollectionAssert.AreEqual(new[] { 6, 5, 4, 3, 2 }, Ids(rows));
			Assert.AreEqual(2, rows[2].Count, "both 'boot' logs fold into the newer one");
			DevLogFilter.Build(SampleLogs, DevLogKinds.Log, "", true, 1, rows);
			Assert.AreEqual(1, rows.Count);
			Assert.AreEqual(1, rows[0].Count, "a capped list still counts only its own group");
		}
	}
}
