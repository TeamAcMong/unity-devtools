using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DreamTech.DevTools.Demo.Tests
{
	/// <summary>The demo driven only through DevTools commands, the way QA or a bot would.</summary>
	public sealed class DemoPlayModeTests
	{
		DemoGame _game;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			PlayerPrefs.DeleteKey(DemoGame.SaveKey);
			DevClock.SetOffset(System.TimeSpan.Zero);
			_game = new GameObject("DemoGame").AddComponent<DemoGame>();
			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			Object.Destroy(_game.gameObject);
			var canvas = GameObject.Find("DemoCanvas"); // only the demo's UI: the DevTools host keeps its own canvas
			if (canvas != null) Object.Destroy(canvas);
			DevAdOutcome.Set(DevAdKind.Rewarded, DevAdMode.Normal);
			DevClock.SetOffset(System.TimeSpan.Zero);
			PlayerPrefs.DeleteKey(DemoGame.SaveKey);
			yield return null;
		}

		static DevResult Run(string line) => DevTools.Registry.Execute(line);

		[UnityTest]
		public IEnumerator AdapterGivesTheStandardCommands()
		{
			Assert.IsTrue(Run("economy.set-balance coins 777").Ok);
			Assert.AreEqual(777, _game.Data.Coins);
			Assert.IsTrue(Run("economy.set-item freeze 9").Ok);
			Assert.AreEqual(9, _game.Data.Freeze);
			Assert.IsTrue(Run("remote-config.override win_coins 1000").Ok);
			Assert.AreEqual(1000, _game.RemoteInt("win_coins", 0));
			Assert.IsTrue(Run("inspector.set DemoSave.Gems 42").Ok);
			Assert.AreEqual(42, _game.Data.Gems);
			yield return null;
		}

		[UnityTest]
		public IEnumerator WinThroughTheLevelPort()
		{
			Assert.IsFalse(Run("level.win").Ok, "no level running yet");
			Run("demo.start-level");
			long coins = _game.Data.Coins;
			Assert.IsTrue(Run("level.win").Ok);
			Assert.AreEqual(2, _game.Data.Level);
			Assert.Greater(_game.Data.Coins, coins);
			yield return null;
		}

		[UnityTest]
		public IEnumerator ForcedAdFailureDeniesTheRevive()
		{
			var run = DevTools.RunScript("demo.start-level; level.lose; waitfor revive-offer 5; ads.rewarded-outcome ForceFail; demo.revive-with-ad");
			for (int i = 0; i < 30 && !run.Finished; i++) yield return null;
			Assert.IsTrue(run.Finished);
			Assert.IsTrue(_game.AwaitingRevive, "revive denied");
			StringAssert.Contains("Ad failed", _game.LastResult);
			Run("ads.rewarded-outcome ForceSuccess");
			Run("demo.revive-with-ad");
			Assert.IsTrue(_game.Playing, "revived");
		}

		[UnityTest]
		public IEnumerator TimeTravelMakesTomorrowsGiftClaimable()
		{
			Assert.IsTrue(Run("demo.claim-daily-gift").Ok);
			Assert.IsFalse(Run("demo.claim-daily-gift").Ok, "once a day");
			Run("time.advance-days 1");
			Assert.IsTrue(Run("demo.claim-daily-gift").Ok);
			Assert.AreEqual(2, _game.Data.GiftStreak);
			yield return null;
		}

		[UnityTest]
		public IEnumerator PresetsAreRegistered()
		{
			Assert.IsTrue(Run("preset \"Rich player\"").Ok);
			for (int i = 0; i < 5; i++) yield return null;
			Assert.AreEqual(99999, _game.Data.Coins);
		}
	}
}
