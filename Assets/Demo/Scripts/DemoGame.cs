using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DreamTech.DevTools.Demo
{
	/// <summary>
	/// A deliberately small "tap the target" game with the usual mobile systems, to show how a game plugs into DevTools:
	/// wallet (coins, gems), boosters (freeze, double), levels with a timer, rewarded-ad revive, a daily gift on
	/// <see cref="DevClock"/>, remote-config values, an A/B test and a JSON save. The UI is built in code (uGUI).
	/// Composition root: <see cref="Awake"/> installs <see cref="DemoDevAdapter"/>; that is the only line a game adds.
	/// </summary>
	public sealed class DemoGame : MonoBehaviour
	{
		[Serializable]
		public sealed class Save
		{
			public int Level = 1;
			public long Coins = 50;
			public long Gems = 3;
			public long Freeze = 1;
			public long Double = 1;
			public string LastGiftDay = "";
			public int GiftStreak;
			public string ForcedGroup = "";
		}

		public const string SaveKey = "DevToolsDemo.Save";

		public Save Data = new Save();
		public bool Playing { get; private set; }
		public bool AwaitingRevive { get; private set; }
		public int Taps { get; private set; }
		public float TimeLeft { get; private set; }
		public string LastResult { get; private set; } = "";
		public int TapsNeeded => RemoteInt("taps_base", 5) + Data.Level * RemoteInt("taps_per_level", 2);
		bool _doubleActive;

		// remote config: defaults + local overrides (a real game would read its remote-config SDK here)
		public readonly Dictionary<string, string> RemoteDefaults = new Dictionary<string, string>
		{
			{ "taps_base", "5" }, { "taps_per_level", "2" }, { "level_seconds", "10" }, { "win_coins", "20" }, { "gift_gems", "2" },
		};

		public readonly Dictionary<string, string> RemoteOverrides = new Dictionary<string, string>();

		public static readonly string[] ButtonGroups = { "green", "orange" };

		public string ButtonGroup => string.IsNullOrEmpty(Data.ForcedGroup) ? ButtonGroups[Mathf.Abs(SystemInfo.deviceUniqueIdentifier.GetHashCode()) % 2] : Data.ForcedGroup;

		Text _status, _wallet, _info;
		Button _tap, _play, _revive, _gift;
		IDisposable _devTools;

		void Awake()
		{
			Load();
			BuildUi();
			_devTools = DevTools.Install(new DemoDevAdapter(this));
		}

		void OnDestroy() => _devTools?.Dispose();

		void Update()
		{
			if (Playing)
			{
				TimeLeft -= Time.deltaTime;
				if (TimeLeft <= 0) Lose();
			}
			Refresh();
		}

		// ---- game flow ----------------------------------------------------------------------------------------------

		public void StartLevel()
		{
			Playing = true;
			AwaitingRevive = false;
			Taps = 0;
			TimeLeft = RemoteInt("level_seconds", 10);
			LastResult = "";
		}

		public void Tap()
		{
			if (!Playing) return;
			Taps += _doubleActive ? 2 : 1;
			if (Taps >= TapsNeeded) Win();
		}

		public void Win()
		{
			if (!Playing) return;
			Playing = false;
			_doubleActive = false;
			Data.Coins += RemoteInt("win_coins", 20);
			LastResult = "Level " + Data.Level + " cleared!";
			Data.Level++;
			SaveNow();
		}

		public void Lose()
		{
			if (!Playing) return;
			Playing = false;
			_doubleActive = false;
			AwaitingRevive = true;
			LastResult = "Time's up on level " + Data.Level + ". Watch an ad to revive?";
		}

		public void ReviveWithAd()
		{
			if (!AwaitingRevive) return;
			DemoAds.ShowRewarded(ok =>
			{
				if (ok)
				{
					Playing = true;
					AwaitingRevive = false;
					TimeLeft = 5;
					LastResult = "Revived: +5 s";
				}
				else LastResult = "Ad failed: no revive";
			});
		}

		public bool UseBooster(string id)
		{
			if (!Playing) return false;
			if (id == "freeze" && Data.Freeze > 0)
			{
				Data.Freeze--;
				TimeLeft += 5;
				return true;
			}
			if (id == "double" && Data.Double > 0 && !_doubleActive)
			{
				Data.Double--;
				_doubleActive = true;
				return true;
			}
			return false;
		}

		public bool GiftAvailable => Data.LastGiftDay != Today;

		static string Today => DevClock.Today.ToString("yyyy-MM-dd");

		/// <summary>Daily gift: once per calendar day of <see cref="DevClock"/> (time travel makes tomorrow testable).</summary>
		public bool ClaimGift()
		{
			if (!GiftAvailable) return false;
			bool consecutive = Data.LastGiftDay == DevClock.Today.AddDays(-1).ToString("yyyy-MM-dd");
			Data.GiftStreak = consecutive ? Data.GiftStreak + 1 : 1;
			Data.LastGiftDay = Today;
			Data.Gems += RemoteInt("gift_gems", 2) * Mathf.Min(Data.GiftStreak, 7);
			SaveNow();
			return true;
		}

		public string Remote(string key) => RemoteOverrides.TryGetValue(key, out var v) ? v : RemoteDefaults.TryGetValue(key, out var d) ? d : "";

		public int RemoteInt(string key, int fallback) => int.TryParse(Remote(key), out int v) ? v : fallback;

		// ---- save ---------------------------------------------------------------------------------------------------

		public void Load()
		{
			string json = PlayerPrefs.GetString(SaveKey, "");
			if (json.Length > 0)
			{
				try { JsonUtility.FromJsonOverwrite(json, Data); }
				catch (Exception e) { Debug.LogWarning("[Demo] bad save: " + e.Message); }
			}
		}

		public void SaveNow()
		{
			PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
			PlayerPrefs.Save();
		}

		void OnApplicationPause(bool paused)
		{
			if (paused) SaveNow();
		}

		void OnApplicationQuit() => SaveNow();

		// ---- UI -----------------------------------------------------------------------------------------------------

		void BuildUi()
		{
			if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
			var canvasGo = new GameObject("DemoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			var canvas = canvasGo.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			var scaler = canvasGo.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1080, 1920);
			scaler.matchWidthOrHeight = 0.5f;
			var root = canvasGo.transform;
			var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

			Text MakeText(string name, Vector2 anchor, int size)
			{
				var go = new GameObject(name, typeof(RectTransform), typeof(Text));
				go.transform.SetParent(root, false);
				var rt = (RectTransform)go.transform;
				rt.anchorMin = rt.anchorMax = anchor;
				rt.sizeDelta = new Vector2(1000, 160);
				var t = go.GetComponent<Text>();
				t.font = font;
				t.fontSize = size;
				t.alignment = TextAnchor.MiddleCenter;
				t.color = Color.white;
				return t;
			}

			Button MakeButton(string name, string label, Vector2 anchor, Vector2 size, Color color, Action onClick)
			{
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(root, false);
				var rt = (RectTransform)go.transform;
				rt.anchorMin = rt.anchorMax = anchor;
				rt.sizeDelta = size;
				go.GetComponent<Image>().color = color;
				var b = go.GetComponent<Button>();
				b.onClick.AddListener(() => onClick());
				var t = MakeText(name + "Label", anchor, 56);
				t.transform.SetParent(go.transform, false);
				var trt = (RectTransform)t.transform;
				trt.anchorMin = Vector2.zero;
				trt.anchorMax = Vector2.one;
				trt.sizeDelta = Vector2.zero;
				t.text = label;
				return b;
			}

			_wallet = MakeText("Wallet", new Vector2(0.5f, 0.92f), 52);
			_status = MakeText("Status", new Vector2(0.5f, 0.8f), 60);
			_info = MakeText("Info", new Vector2(0.5f, 0.68f), 44);
			_tap = MakeButton("TapButton", "TAP!", new Vector2(0.5f, 0.45f), new Vector2(600, 600), ButtonGroup == "green" ? new Color(0.2f, 0.7f, 0.3f) : new Color(0.95f, 0.55f, 0.1f), Tap);
			_play = MakeButton("PlayButton", "Play", new Vector2(0.5f, 0.2f), new Vector2(600, 160), new Color(0.2f, 0.4f, 0.8f), StartLevel);
			_revive = MakeButton("ReviveButton", "Revive (ad)", new Vector2(0.5f, 0.2f), new Vector2(600, 160), new Color(0.7f, 0.2f, 0.6f), ReviveWithAd);
			_gift = MakeButton("GiftButton", "Daily gift", new Vector2(0.5f, 0.08f), new Vector2(600, 120), new Color(0.8f, 0.6f, 0.1f), () => ClaimGift());
			MakeButton("FreezeButton", "Freeze", new Vector2(0.2f, 0.3f), new Vector2(260, 120), new Color(0.3f, 0.6f, 0.9f), () => UseBooster("freeze"));
			MakeButton("DoubleButton", "x2", new Vector2(0.8f, 0.3f), new Vector2(260, 120), new Color(0.9f, 0.3f, 0.3f), () => UseBooster("double"));
		}

		void Refresh()
		{
			if (_wallet == null) return;
			_wallet.text = "Coins " + Data.Coins + "   Gems " + Data.Gems + "   Freeze " + Data.Freeze + "   x2 " + Data.Double;
			_status.text = Playing ? "Level " + Data.Level + ":  " + Taps + " / " + TapsNeeded + "   " + Mathf.CeilToInt(TimeLeft) + " s" : "Level " + Data.Level;
			_info.text = LastResult + (GiftAvailable ? "\nDaily gift ready" : "\nGift streak " + Data.GiftStreak + " (next: tomorrow)");
			_tap.interactable = Playing;
			_play.gameObject.SetActive(!Playing && !AwaitingRevive);
			_revive.gameObject.SetActive(AwaitingRevive);
			_gift.interactable = GiftAvailable;
		}
	}

	/// <summary>The demo's "ad SDK": always fills after a short delay, unless DevTools forces an outcome.</summary>
	public static class DemoAds
	{
		public static bool RewardedReady => DevAdOutcome.IsReady(DevAdKind.Rewarded, true);

		public static void ShowRewarded(Action<bool> done)
		{
			if (DevAdOutcome.TryIntercept(DevAdKind.Rewarded, out bool forced))
			{
				done(forced);
				return;
			}
			done(true); // a real game calls its SDK here
		}

		public static void ShowInterstitial(Action<bool> done)
		{
			if (DevAdOutcome.TryIntercept(DevAdKind.Interstitial, out bool forced))
			{
				done(forced);
				return;
			}
			done(true);
		}
	}
}
