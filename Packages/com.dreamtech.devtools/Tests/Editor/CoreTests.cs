using System;
using System.Linq;
using NUnit.Framework;

namespace DreamTech.DevTools.Tests
{
	public sealed class RegistryTests
	{
		DevRegistry _r;

		[SetUp]
		public void SetUp() => _r = new DevRegistry();

		[Test]
		public void Tokenize_GroupsQuotedTextAndKeepsEmptyQuotes()
		{
			CollectionAssert.AreEqual(new[] { "a", "b c", "", "d" }, DevRegistry.Tokenize("a \"b c\" \"\"  d"));
			CollectionAssert.IsEmpty(DevRegistry.Tokenize("   "));
		}

		[Test]
		public void Ids_AreCategoryAndLabelSlugs()
		{
			var c = _r.Action("Rank event", "Reset today's spins", () => DevResult.Success());
			Assert.AreEqual("rank-event.reset-today-s-spins", c.Id);
		}

		[Test]
		public void Execute_TypesArgumentsAndFillsDefaults()
		{
			long got = 0;
			string text = null;
			_r.Action("A", "Set", new[] { DevParam.Int("n", 7), DevParam.Text("t", "x") }, a =>
			{
				got = a.Long(0);
				text = a.Str(1);
				return DevResult.Success();
			});
			Assert.IsTrue(_r.Execute("a.set 42").Ok);
			Assert.AreEqual(42, got);
			Assert.AreEqual("x", text);
			Assert.IsTrue(_r.Execute("a.set").Ok);
			Assert.AreEqual(7, got);
		}

		[Test]
		public void Execute_BadArgumentFailsWithTheParameterName()
		{
			_r.Action("A", "Set", new[] { DevParam.Int("amount") }, a => DevResult.Success(a.Int(0).ToString()));
			var res = _r.Execute("a.set lots");
			Assert.IsFalse(res.Ok);
			StringAssert.Contains("amount", res.Message);
		}

		[Test]
		public void Execute_ExceptionBecomesFailureAndIsReported()
		{
			Exception seen = null;
			_r.CommandException += e => seen = e;
			_r.Action("A", "Boom", () => throw new InvalidOperationException("kaput"));
			var res = _r.Execute("a.boom");
			Assert.IsFalse(res.Ok);
			StringAssert.Contains("kaput", res.Message);
			Assert.IsInstanceOf<InvalidOperationException>(seen);
		}

		[Test]
		public void Blocked_CommandDoesNotRun()
		{
			bool ran = false;
			_r.Action("A", "Go", () =>
			{
				ran = true;
				return DevResult.Success();
			}, blocked: () => "not now");
			var res = _r.Execute("a.go");
			Assert.IsFalse(ran);
			Assert.AreEqual("not now", res.Message);
		}

		[Test]
		public void Resolve_AcceptsUniquePrefixAndReportsAmbiguity()
		{
			_r.Action("Level", "Win", () => DevResult.Success("w"));
			_r.Action("Level", "Jump", () => DevResult.Success("j"));
			Assert.AreEqual("w", _r.Execute("level.w").Message);
			var res = _r.Execute("level.");
			Assert.IsFalse(res.Ok);
			StringAssert.Contains("ambiguous", res.Message);
			StringAssert.Contains("unknown", _r.Execute("nope").Message);
		}

		[Test]
		public void Toggle_FlipsWithoutArgumentAndAcceptsOnOff()
		{
			bool v = false;
			_r.Toggle("A", "God", () => v, x => v = x);
			_r.Execute("a.god");
			Assert.IsTrue(v);
			_r.Execute("a.god off");
			Assert.IsFalse(v);
			_r.Execute("a.god on");
			Assert.IsTrue(v);
		}

		[Test]
		public void SameId_NewestRegistrationWins()
		{
			_r.Action("A", "Go", () => DevResult.Success("old"));
			_r.Action("A", "Go", () => DevResult.Success("new"));
			Assert.AreEqual(1, _r.Commands.Count);
			Assert.AreEqual("new", _r.Execute("a.go").Message);
		}

		[Test]
		public void Remove_DropsEverythingOfTheOwnerAndEmptyCategories()
		{
			var owner = new object();
			_r.RegisterModule(new InlineModule(r =>
			{
				r.Action("Mine", "Go", () => DevResult.Success());
				r.Watch("Mine", "V", () => "1");
				r.Condition("mine", () => true);
				r.Preset("mine", "mine.go");
			}), owner);
			_r.Action("Other", "Stay", () => DevResult.Success());
			_r.Remove(owner);
			Assert.IsNull(_r.Find("mine.go"));
			Assert.IsEmpty(_r.Watches);
			Assert.IsFalse(_r.TryCondition("mine", out _));
			Assert.IsEmpty(_r.Presets);
			CollectionAssert.AreEqual(new[] { "Other" }, _r.Categories);
		}

		[Test]
		public void RegisterModules_OnlyTakesMarkedModules()
		{
			_r.RegisterModules(new[] { typeof(RegistryTests).Assembly });
			Assert.IsNotNull(_r.Find("test-discovery.marked"));
			Assert.IsNull(_r.Find("test-discovery.unmarked"));
		}

		[Test]
		public void EveryExecutionIsLogged()
		{
			_r.Action("A", "Go", () => DevResult.Success("done"));
			_r.Execute("a.go");
			_r.Post("later", false, "failed later");
			Assert.AreEqual(2, _r.Log.Count);
			Assert.AreEqual("a.go", _r.Log[0].Line);
			Assert.IsFalse(_r.Log[1].Ok);
		}

		sealed class InlineModule : IDevModule
		{
			readonly Action<DevRegistry> _register;
			public InlineModule(Action<DevRegistry> register) => _register = register;
			public void Register(DevRegistry r) => _register(r);
		}
	}

	public sealed class PortTests
	{
		FakeGame _game;
		IDisposable _handle;

		[SetUp]
		public void SetUp()
		{
			DevTools.Reset();
			DevTools.Registry.RegisterModules(new[] { typeof(DevTools).Assembly }); // the built-in Inspector / Tools modules
			_game = new FakeGame();
			_handle = DevTools.Install(_game);
		}

		[TearDown]
		public void TearDown() => DevTools.Reset();

		static DevResult Run(string line) => DevTools.Registry.Execute(line);

		[Test]
		public void Currency_SetAddAndSetAll()
		{
			Assert.IsTrue(Run("economy.set-balance gems 50").Ok);
			Assert.AreEqual(50, _game.Wallet["gems"]);
			Run("economy.add-balance coins -500");
			Assert.AreEqual(0, _game.Wallet["coins"], "never below zero");
			Run("economy.set-all-balances 9");
			Assert.IsTrue(_game.Wallet.Values.All(v => v == 9));
			Assert.IsFalse(Run("economy.set-balance diamonds 1").Ok);
		}

		[Test]
		public void Inventory_SetAndSetAll()
		{
			Run("economy.set-item hint 7");
			Assert.AreEqual(7, _game.Items["hint"]);
			Run("economy.set-all-items 0");
			Assert.IsTrue(_game.Items.Values.All(v => v == 0));
		}

		[Test]
		public void Level_WinLoseNeedAPlayingLevel()
		{
			Assert.IsTrue(Run("level.win").Ok);
			Assert.AreEqual(1, _game.Wins);
			var res = Run("level.lose");
			Assert.IsFalse(res.Ok, "no level running after the win");
			Assert.AreEqual(0, _game.Losses);
			Run("level.jump-to 20");
			Assert.AreEqual(20, _game.Level);
			Assert.IsTrue(DevTools.Registry.TryCondition("playing", out var playing));
			Assert.IsFalse(playing());
		}

		[Test]
		public void RemoteConfig_OverrideAndClear()
		{
			Run("remote-config.override lives_max 99");
			Assert.AreEqual("99", _game.GetValue("lives_max"));
			Run("remote-config.clear-all-overrides");
			Assert.AreEqual("5", _game.GetValue("lives_max"));
		}

		[Test]
		public void Experiments_ForceGroupFromPair()
		{
			Assert.IsTrue(Run("experiments.force-group \"shop_layout = b\"").Ok);
			Assert.AreEqual("b", _game.Forced["shop_layout"]);
		}

		[Test]
		public void Save_ObjectsAreInspectableAndEditable()
		{
			Assert.IsTrue(Run("inspector.set PlayerSave.Coins 500").Ok);
			Assert.AreEqual(500, _game.Save.Coins);
			StringAssert.Contains("Tutorial", Run("inspector.show PlayerSave").Message);
			Run("save.save-now");
			Assert.AreEqual(1, _game.Saves);
		}

		[Test]
		public void Ads_ResultArrivesInTheLog()
		{
			Assert.IsTrue(Run("ads.show-interstitial").Ok, "the request itself succeeds");
			// the fake answers synchronously, so its posted result is logged just before the command line
			Assert.IsTrue(DevTools.Registry.Log.Any(e => e.Line == "ads.show-interstitial" && !e.Ok && e.Message.Contains("not shown")));
		}

		[Test]
		public void Dispose_RemovesEveryPortCommand()
		{
			_handle.Dispose();
			Assert.IsNull(DevTools.Registry.Find("economy.set-balance"));
			Assert.IsNull(DevTools.Registry.Find("level.win"));
			Assert.IsFalse(DevTools.Registry.TryCondition("playing", out _));
		}

		[Test]
		public void InstallingAgain_ReplacesInsteadOfDuplicating()
		{
			int before = DevTools.Registry.Commands.Count;
			DevTools.Install(_game);
			Assert.AreEqual(before, DevTools.Registry.Commands.Count);
		}

		[Test]
		public void PortsOf_ListsImplementedPorts()
		{
			CollectionAssert.IsSupersetOf(DevTools.PortsOf(_game).ToList(), new[] { "ICurrencyPort", "ILevelPort", "ISaveSourcePort" });
		}
	}

	public sealed class ScriptTests
	{
		DevRegistry _r;
		double _now;
		int _count;
		bool _ready;

		[SetUp]
		public void SetUp()
		{
			_r = new DevRegistry { Clock = () => _now };
			_now = 0;
			_count = 0;
			_ready = false;
			_r.Action("T", "Inc", () => DevResult.Success((++_count).ToString()));
			_r.Condition("ready", () => _ready);
		}

		[Test]
		public void CommandsRunOnePerTick()
		{
			var s = new DevScriptRun(_r, "t.inc; t.inc\n# comment\nt.inc");
			Assert.AreEqual(3, s.StepCount);
			Assert.IsFalse(s.Tick(0));
			Assert.AreEqual(1, _count);
			s.Tick(0);
			Assert.IsTrue(s.Tick(0));
			Assert.AreEqual(3, _count);
		}

		[Test]
		public void WaitPausesForRealTime()
		{
			var s = new DevScriptRun(_r, "wait 2; t.inc");
			Assert.IsFalse(s.Tick(0));
			Assert.IsFalse(s.Tick(1.9));
			Assert.AreEqual(0, _count);
			Assert.IsTrue(s.Tick(2.0));
			Assert.AreEqual(1, _count);
		}

		[Test]
		public void WaitForHoldsUntilTheConditionThenContinues()
		{
			var s = new DevScriptRun(_r, "waitfor ready 10; t.inc");
			Assert.IsFalse(s.Tick(0));
			Assert.IsFalse(s.Tick(5));
			_ready = true;
			Assert.IsTrue(s.Tick(6));
			Assert.AreEqual(1, _count);
			Assert.IsTrue(_r.Log.Any(e => e.Ok && e.Line.Contains("waitfor ready")));
		}

		[Test]
		public void WaitForTimesOutAndCarriesOn()
		{
			var s = new DevScriptRun(_r, "waitfor ready 3; t.inc");
			s.Tick(0);
			Assert.IsFalse(s.Tick(3));
			Assert.IsTrue(_r.Log.Any(e => !e.Ok && e.Message.StartsWith("TIMEOUT", StringComparison.Ordinal)));
			Assert.IsTrue(s.Tick(3.1));
			Assert.AreEqual(1, _count);
		}

		[Test]
		public void UnknownConditionIsReportedAndSkipped()
		{
			var s = new DevScriptRun(_r, "waitfor nope; t.inc");
			Assert.IsTrue(s.Tick(0));
			Assert.AreEqual(1, _count);
			Assert.IsTrue(_r.Log.Any(e => !e.Ok && e.Message.Contains("unknown condition")));
		}

		[Test]
		public void LooksLikeScript()
		{
			Assert.IsTrue(DevScriptRun.LooksLikeScript("a; b"));
			Assert.IsTrue(DevScriptRun.LooksLikeScript("wait 1"));
			Assert.IsTrue(DevScriptRun.LooksLikeScript("waitfor home"));
			Assert.IsFalse(DevScriptRun.LooksLikeScript("economy.set-balance coins 5"));
		}

		[Test]
		public void DevToolsTick_RunsAndDropsFinishedScripts()
		{
			DevTools.Reset();
			try
			{
				DevTools.Registry.Clock = () => _now;
				DevTools.Registry.Action("T", "Inc", () => DevResult.Success((++_count).ToString()));
				DevTools.RunScript("t.inc; t.inc");
				DevTools.Tick();
				DevTools.Tick();
				Assert.AreEqual(2, _count);
				Assert.AreEqual(0, DevTools.RunningScripts.Count);
			}
			finally { DevTools.Reset(); }
		}
	}

	public sealed class InspectorTests
	{
		[Test]
		public void ReadsAndWritesNestedMembersListsAndDictionaries()
		{
			var save = new PlayerSave();
			Assert.IsTrue(ObjectInspector.TrySet(save, "Stats.Accuracy", "0.75", out _));
			Assert.AreEqual(0.75f, save.Stats.Accuracy);
			Assert.IsTrue(ObjectInspector.TrySet(save, "Stars[1]", "3", out _));
			Assert.AreEqual(3, save.Stars[1]);
			Assert.IsTrue(ObjectInspector.TrySet(save, "Flags[\"intro\"]", "0", out _));
			Assert.AreEqual(0, save.Flags["intro"]);
			Assert.IsTrue(ObjectInspector.TrySet(save, "difficulty", "hard", out _), "member and enum names ignore case");
			Assert.AreEqual(Difficulty.Hard, save.Difficulty);
			Assert.IsTrue(ObjectInspector.TrySet(save, "Tutorial", "off", out _));
			Assert.IsFalse(save.Tutorial);
		}

		[Test]
		public void RefusesBadPathsReadOnlyMembersAndBadValues()
		{
			var save = new PlayerSave();
			Assert.IsFalse(ObjectInspector.TrySet(save, "Nope", "1", out string m1));
			StringAssert.Contains("no public member", m1);
			Assert.IsFalse(ObjectInspector.TrySet(save, "ReadOnlyName", "x", out string m2));
			StringAssert.Contains("read-only", m2);
			Assert.IsFalse(ObjectInspector.TrySet(save, "Coins", "many", out _));
			Assert.IsFalse(ObjectInspector.TrySet(save, "Stars[9]", "1", out string m3));
			StringAssert.Contains("out of range", m3);
			Assert.AreEqual(10, save.Coins);
		}

		[Test]
		public void SplitKeepsDotsInsideQuotedKeys()
		{
			CollectionAssert.AreEqual(new[] { "A", "B", "[2]", "C", "[\"k.x\"]" }, ObjectInspector.Split("A.B[2].C[\"k.x\"]"));
		}
	}

	public sealed class ClockAndAdsTests
	{
		[SetUp]
		public void SetUp() => DevTools.Reset();

		[TearDown]
		public void TearDown() => DevTools.Reset();

		[Test]
		public void OffsetShiftsNowAndRaisesTheEvent()
		{
			int changes = 0;
			DevClock.OffsetChanged += () => changes++;
			var before = DateTime.Now;
			DevClock.SetOffset(TimeSpan.FromDays(2));
			Assert.AreEqual(1, changes);
			Assert.That((DevClock.Now - before).TotalDays, Is.InRange(1.99, 2.01));
			DevClock.SetOffset(TimeSpan.FromDays(2));
			Assert.AreEqual(1, changes, "same offset: no event");
		}

		[Test]
		public void TimeCommandsMoveTheClock()
		{
			DevTools.Registry.RegisterModules(new[] { typeof(DevTools).Assembly });
			DevTools.Registry.Execute("time.advance-days 3");
			Assert.AreEqual(TimeSpan.FromDays(3), DevClock.Offset);
			DevTools.Registry.Execute("time.reset");
			Assert.AreEqual(TimeSpan.Zero, DevClock.Offset);
		}

		[Test]
		public void AdOutcome_InterceptsOnlyWhenForced()
		{
			Assert.IsFalse(DevAdOutcome.TryIntercept(DevAdKind.Rewarded, out _));
			DevAdOutcome.Set(DevAdKind.Rewarded, DevAdMode.ForceFail);
			Assert.IsTrue(DevAdOutcome.TryIntercept(DevAdKind.Rewarded, out bool ok));
			Assert.IsFalse(ok);
			DevAdOutcome.Set(DevAdKind.Rewarded, DevAdMode.ForceNoFill);
			Assert.IsFalse(DevAdOutcome.IsReady(DevAdKind.Rewarded, true));
			Assert.AreEqual(2, DevAdOutcome.Requests(DevAdKind.Rewarded));
			Assert.IsTrue(DevAdOutcome.IsReady(DevAdKind.Interstitial, true));
		}
	}
}
