﻿using NStack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;
using TMAPT.Core;
using TMAPT.Display.Advanced.Views;
using Rune = System.Rune;

namespace TMAPT.Display
{
	class StateMachine
	{
		private static Toplevel _top;
		private static MenuBar _menu;
		private static int _nameColumnWidth;
		private static FrameView _leftPane;
		private static List<string> _categories;
		private static ListView _categoryListView;
		private static FrameView _rightPane;
		private static List<Type> _scenarios;
		private static ListView _scenarioListView { get; set; }
		private static StatusBar _statusBar;
		private static StatusItem _capslock;
		private static StatusItem _numlock;
		private static StatusItem _scrolllock;
		private static int _categoryListViewItem;
		private static int _scenarioListViewItem;

		private static Scenario Scenario = null;
		private static bool _useSystemConsole = false;
		private static ConsoleDriver.DiagnosticFlags _diagnosticFlags;
		private static bool _heightAsBuffer = false;
		private static bool _alwaysSetPosition;

		private CoreLib Core { get; set; }

		public Task Start(string[] args)
        {
            Console.OutputEncoding = Encoding.Default;

			if (Debugger.IsAttached)
				CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

			_scenarios = Scenario.GetDerivedClasses<Scenario>().OrderBy(t => Scenario.ScenarioMetadata.GetName(t)).ToList();

			if (args.Length > 0 && args.Contains("-usc"))
			{
				_useSystemConsole = true;
				args = args.Where(val => val != "-usc").ToArray();
			}
			if (args.Length > 0)
			{
				// Implement argument
			}
            else
            {
				// Default startup
				int index = _scenarios.FindIndex(x => x.Name == "GraphWindow");
				Scenario = new FullMetricWindow(Core = new CoreLib());
				Application.UseSystemConsole = _useSystemConsole;
				Application.Init();
				Scenario.Init(Application.Top, Colors.TopLevel);

				Application.MainLoop.Invoke(() => {				
					Core.Initiate();
				});

				Scenario.Setup();
				Scenario.Run();
				Scenario = null;
			}


			// Run StateMachine
			while ((Scenario = MainScenario()) != null)
			{
				#if DEBUG_IDISPOSABLE
				// Validate there are no outstanding Responder-based instances gui 
				// after a scenario was selected to run. This proves the main UI Catalog
				// 'app' closed cleanly.
				
				foreach (var inst in Responder.Instances) {
					Debug.Assert (inst.WasDisposed);
				}
				Responder.Instances.Clear ();
				#endif

				Scenario.Init(Application.Top, _baseColorScheme);
				Scenario.Setup();
				// 17. Application top layer Add
				Scenario.Run();


				static void LoadedHandler()
				{
					_rightPane.SetFocus();
					_top.Loaded -= LoadedHandler;
				}
				_top.Loaded += LoadedHandler;

				#if DEBUG_IDISPOSABLE
				// After the scenario runs, validate all Responder-based instances
				// were disposed. This proves the scenario 'app' closed cleanly.
				foreach (var inst in Responder.Instances) {
					Debug.Assert (inst.WasDisposed);
				}
				Responder.Instances.Clear ();
				#endif
			}

			Application.Shutdown();
            return Task.CompletedTask;
        }

        public void CreateScenario(int index) {
			index = _scenarios.FindIndex(x => x.Name == "MainWindow");
			Scenario = Activator.CreateInstance(_scenarios[index], Core) as Scenario;
			Application.UseSystemConsole = _useSystemConsole;
			Application.Init();
			Scenario.Init(Application.Top, Colors.TopLevel);
			Scenario.Setup();
			Scenario.Run();
			Scenario = null;

		}
		/// <summary>
		/// This shows the selection UI. Each time it is run, it calls Application.Init to reset everything.
		/// </summary>
		/// <returns></returns>
		private Scenario MainScenario()
		{
			// Create GUI first
			// 1. Set Console / Application settings
			Application.UseSystemConsole = _useSystemConsole;
			Application.Init();
			Application.HeightAsBuffer = _heightAsBuffer;
			Application.AlwaysSetPosition = _alwaysSetPosition;


			// 2. Set colorscheme - Set this here because not initialized until driver is loaded
			_baseColorScheme = Colors.TopLevel;

			// 3. About string builder
			StringBuilder aboutMessage = new StringBuilder();
			aboutMessage.AppendLine("UI Catalog is a comprehensive sample library for Terminal.Gui");
			aboutMessage.AppendLine(@"             _           ");
			aboutMessage.AppendLine(@"  __ _ _   _(_)  ___ ___ ");
			aboutMessage.AppendLine(@" / _` | | | | | / __/ __|");
			aboutMessage.AppendLine(@"| (_| | |_| | || (__\__ \");
			aboutMessage.AppendLine(@" \__, |\__,_|_(_)___|___/");
			aboutMessage.AppendLine(@" |___/                   ");
			aboutMessage.AppendLine("");
			aboutMessage.AppendLine($"Version: {typeof(StateMachine).Assembly.GetName().Version}");
			aboutMessage.AppendLine($"Using Terminal.Gui Version: {FileVersionInfo.GetVersionInfo(typeof(Terminal.Gui.Application).Assembly.Location).ProductVersion}");
			aboutMessage.AppendLine("");

			// 4. Menubar
			_menu = new MenuBar(new MenuBarItem[] {
				new MenuBarItem ("_File", new MenuItem [] {
					new MenuItem ("_Quit", "", () => Application.RequestStop(), null, null, Key.Q | Key.CtrlMask)
				}),
				new MenuBarItem ("_Color Scheme", CreateColorSchemeMenuItems()),
				new MenuBarItem ("Diag_nostics", CreateDiagnosticMenuItems()),
				new MenuBarItem ("_Help", new MenuItem [] {
					new MenuItem ("_gui.cs API Overview", "", () => OpenUrl ("https://migueldeicaza.github.io/gui.cs/articles/overview.html"), null, null, Key.F1),
					new MenuItem ("gui.cs _README", "", () => OpenUrl ("https://github.com/migueldeicaza/gui.cs"), null, null, Key.F2),
					new MenuItem ("_About...", "About this app", () =>  MessageBox.Query ("About UI Catalog", aboutMessage.ToString(), "_Ok"), null, null, Key.CtrlMask | Key.A),
				})
			});

			// 5. Leftpane Controller
			_leftPane = new FrameView("Categories")
			{
				X = 0,
				Y = 1, // for menu
				Width = 25,
				Height = Dim.Fill(1),
				CanFocus = false,
				Shortcut = Key.CtrlMask | Key.C
			};
			_leftPane.Title = $"{_leftPane.Title} ({_leftPane.ShortcutTag})";
			_leftPane.ShortcutAction = () => _leftPane.SetFocus();

			// 6. Get all categories
			_categories = Scenario.GetAllCategories().OrderBy(c => c).ToList();
			_categoryListView = new ListView(_categories)
			{
				X = 0,
				Y = 0,
				Width = Dim.Fill(0),
				Height = Dim.Fill(0),
				AllowsMarking = false,
				CanFocus = true,
			};
			_categoryListView.OpenSelectedItem += (a) => {
				_rightPane.SetFocus();
			};

			// Event listener
			 _categoryListView.SelectedItemChanged += categoryList_SelectedChanged;
			_leftPane.Add(_categoryListView);

			// 7. Rightpane scenariolist
			_rightPane = new FrameView("Scenarios GUI")
			{
				X = 25,
				Y = 1, // for menu
				Width = Dim.Fill(),
				Height = Dim.Fill(1),
				CanFocus = true,
				Shortcut = Key.CtrlMask | Key.S
			};
			_rightPane.Title = $"{_rightPane.Title} ({_rightPane.ShortcutTag})";
			_rightPane.ShortcutAction = () => _rightPane.SetFocus();

			// 8. Scenario metadata get name
			_nameColumnWidth = Scenario.ScenarioMetadata.GetName(_scenarios.OrderByDescending(t => Scenario.ScenarioMetadata.GetName(t).Length).FirstOrDefault()).Length;

			// 9. All scenarios listview
			_scenarioListView = new ListView()
			{
				X = 0,
				Y = 0,
				Width = Dim.Fill(0),
				Height = Dim.Fill(0),
				AllowsMarking = false,
				CanFocus = true,
			};

			// 10. scenario eventlistener
			_scenarioListView.OpenSelectedItem += _scenarioOpenSelectedItem;
			// 11. Add listview to rightpane
			_rightPane.Add(_scenarioListView);

			// 12. CategoryList eventListener
			_categoryListView.SelectedItem = _categoryListViewItem;
			_categoryListView.OnSelectedChanged();

			// 13. KeyMasking - StatusItem
			_capslock = new StatusItem(Key.CharMask, "Caps", null);
			_numlock = new StatusItem(Key.CharMask, "Num", null);
			_scrolllock = new StatusItem(Key.CharMask, "Scroll", null);

			// 14. BottomStatusBar
			_statusBar = new StatusBar()
			{
				Visible = true,
			};
			/// 14.1. Status item
			_statusBar.Items = new StatusItem[] {
				_capslock,
				_numlock,
				_scrolllock,
				new StatusItem(Key.Q | Key.CtrlMask, "~CTRL-Q~ Quit", () => {
					if (Scenario is null){
						// This causes GetScenarioToRun to return null
						Scenario = null;
						Application.RequestStop();
					} else {
						Scenario.RequestStop();
					}
				}),
				new StatusItem(Key.F10, "~F10~ Hide/Show Status Bar", () => {
					_statusBar.Visible = !_statusBar.Visible;
					_leftPane.Height = Dim.Fill(_statusBar.Visible ? 1 : 0);
					_rightPane.Height = Dim.Fill(_statusBar.Visible ? 1 : 0);
					_top.LayoutSubviews();
					_top.SetChildNeedsDisplay();
				}),
				new StatusItem (Key.CharMask, Application.Driver.GetType().Name, null),
			};

			// 15. SetColorScheme
			SetColorScheme();

			// 16. Application TopLayer
			_top = Application.Top;
			_top.KeyDown += KeyDownHandler;
			_top.Add(_menu);
			_top.Add(_leftPane);
			_top.Add(_rightPane);
			_top.Add(_statusBar);

			_top.Loaded += () => {
				if (Scenario != null)
				{
					//Scenario.Dispose();
					Scenario = null;
				}
			};

			Application.Run(_top);

			return Scenario;
		}
		static List<MenuItem[]> CreateDiagnosticMenuItems()
		{
			List<MenuItem[]> menuItems = new List<MenuItem[]>();
			menuItems.Add(CreateDiagnosticFlagsMenuItems());
			menuItems.Add(new MenuItem[] { null });
			menuItems.Add(CreateSizeStyle());
			menuItems.Add(CreateAlwaysSetPosition());
			return menuItems;
		}
		static MenuItem[] CreateAlwaysSetPosition()
		{
			List<MenuItem> menuItems = new List<MenuItem>();
			var item = new MenuItem();
			item.Title = "_Always set position (NetDriver only)";
			item.Shortcut = Key.CtrlMask | Key.AltMask | (Key)item.Title.ToString().Substring(1, 1)[0];
			item.CheckType |= MenuItemCheckStyle.Checked;
			item.Checked = Application.AlwaysSetPosition;
			item.Action += () => {
				Application.AlwaysSetPosition = !item.Checked;
				item.Checked = _alwaysSetPosition = Application.AlwaysSetPosition;
			};
			menuItems.Add(item);

			return menuItems.ToArray();
		}
		static MenuItem[] CreateSizeStyle()
		{
			List<MenuItem> menuItems = new List<MenuItem>();
			var item = new MenuItem();
			item.Title = "_Height As Buffer";
			item.Shortcut = Key.CtrlMask | Key.AltMask | (Key)item.Title.ToString().Substring(1, 1)[0];
			item.CheckType |= MenuItemCheckStyle.Checked;
			item.Checked = Application.HeightAsBuffer;
			item.Action += () => {
				item.Checked = !item.Checked;
				_heightAsBuffer = item.Checked;
				Application.HeightAsBuffer = _heightAsBuffer;
			};
			menuItems.Add(item);

			return menuItems.ToArray();
		}
		static MenuItem[] CreateDiagnosticFlagsMenuItems()
		{
			const string OFF = "Diagnostics: _Off";
			const string FRAME_RULER = "Diagnostics: Frame _Ruler";
			const string FRAME_PADDING = "Diagnostics: _Frame Padding";
			var index = 0;

			List<MenuItem> menuItems = new List<MenuItem>();
			foreach (Enum diag in Enum.GetValues(_diagnosticFlags.GetType()))
			{
				var item = new MenuItem();
				item.Title = GetDiagnosticsTitle(diag);
				item.Shortcut = Key.AltMask + index.ToString()[0];
				index++;
				item.CheckType |= MenuItemCheckStyle.Checked;
				if (GetDiagnosticsTitle(ConsoleDriver.DiagnosticFlags.Off) == item.Title)
				{
					item.Checked = (_diagnosticFlags & (ConsoleDriver.DiagnosticFlags.FramePadding
					| ConsoleDriver.DiagnosticFlags.FrameRuler)) == 0;
				}
				else
				{
					item.Checked = _diagnosticFlags.HasFlag(diag);
				}
				item.Action += () => {
					var t = GetDiagnosticsTitle(ConsoleDriver.DiagnosticFlags.Off);
					if (item.Title == t && !item.Checked)
					{
						_diagnosticFlags &= ~(ConsoleDriver.DiagnosticFlags.FramePadding | ConsoleDriver.DiagnosticFlags.FrameRuler);
						item.Checked = true;
					}
					else if (item.Title == t && item.Checked)
					{
						_diagnosticFlags |= (ConsoleDriver.DiagnosticFlags.FramePadding | ConsoleDriver.DiagnosticFlags.FrameRuler);
						item.Checked = false;
					}
					else
					{
						var f = GetDiagnosticsEnumValue(item.Title);
						if (_diagnosticFlags.HasFlag(f))
						{
							SetDiagnosticsFlag(f, false);
						}
						else
						{
							SetDiagnosticsFlag(f, true);
						}
					}
					foreach (var menuItem in menuItems)
					{
						if (menuItem.Title == t)
						{
							menuItem.Checked = !_diagnosticFlags.HasFlag(ConsoleDriver.DiagnosticFlags.FrameRuler)
								&& !_diagnosticFlags.HasFlag(ConsoleDriver.DiagnosticFlags.FramePadding);
						}
						else if (menuItem.Title != t)
						{
							menuItem.Checked = _diagnosticFlags.HasFlag(GetDiagnosticsEnumValue(menuItem.Title));
						}
					}
					ConsoleDriver.Diagnostics = _diagnosticFlags;
					_top.SetNeedsDisplay();
				};
				menuItems.Add(item);
			}
			return menuItems.ToArray();

			string GetDiagnosticsTitle(Enum diag)
			{
				switch (Enum.GetName(_diagnosticFlags.GetType(), diag))
				{
					case "Off":
						return OFF;
					case "FrameRuler":
						return FRAME_RULER;
					case "FramePadding":
						return FRAME_PADDING;
				}
				return "";
			}

			Enum GetDiagnosticsEnumValue(ustring title)
			{
				switch (title.ToString())
				{
					case FRAME_RULER:
						return ConsoleDriver.DiagnosticFlags.FrameRuler;
					case FRAME_PADDING:
						return ConsoleDriver.DiagnosticFlags.FramePadding;
				}
				return null;
			}

			void SetDiagnosticsFlag(Enum diag, bool add)
			{
				switch (diag)
				{
					case ConsoleDriver.DiagnosticFlags.FrameRuler:
						if (add)
						{
							_diagnosticFlags |= ConsoleDriver.DiagnosticFlags.FrameRuler;
						}
						else
						{
							_diagnosticFlags &= ~ConsoleDriver.DiagnosticFlags.FrameRuler;
						}
						break;
					case ConsoleDriver.DiagnosticFlags.FramePadding:
						if (add)
						{
							_diagnosticFlags |= ConsoleDriver.DiagnosticFlags.FramePadding;
						}
						else
						{
							_diagnosticFlags &= ~ConsoleDriver.DiagnosticFlags.FramePadding;
						}
						break;
					default:
						_diagnosticFlags = default;
						break;
				}
			}

			//MenuItem CheckedMenuMenuItem (ustring menuItem, Action action, Func<bool> checkFunction)
			//{
			//	var mi = new MenuItem ();
			//	mi.Title = menuItem;
			//	mi.Shortcut = Key.AltMask + index.ToString () [0];
			//	index++;
			//	mi.CheckType |= MenuItemCheckStyle.Checked;
			//	mi.Checked = checkFunction ();
			//	mi.Action = () => {
			//		action?.Invoke ();
			//		mi.Title = menuItem;
			//		mi.Checked = checkFunction ();
			//	};
			//	return mi;
			//}

			//return new MenuItem [] {
			//	CheckedMenuMenuItem ("Use _System Console",
			//		() => {
			//			_useSystemConsole = !_useSystemConsole;
			//		},
			//		() => _useSystemConsole),
			//	CheckedMenuMenuItem ("Diagnostics: _Frame Padding",
			//		() => {
			//			ConsoleDriver.Diagnostics ^= ConsoleDriver.DiagnosticFlags.FramePadding;
			//			_top.SetNeedsDisplay ();
			//		},
			//		() => (ConsoleDriver.Diagnostics & ConsoleDriver.DiagnosticFlags.FramePadding) == ConsoleDriver.DiagnosticFlags.FramePadding),
			//	CheckedMenuMenuItem ("Diagnostics: Frame _Ruler",
			//		() => {
			//			ConsoleDriver.Diagnostics ^= ConsoleDriver.DiagnosticFlags.FrameRuler;
			//			_top.SetNeedsDisplay ();
			//		},
			//		() => (ConsoleDriver.Diagnostics & ConsoleDriver.DiagnosticFlags.FrameRuler) == ConsoleDriver.DiagnosticFlags.FrameRuler),
			//};
		}
		static void SetColorScheme()
		{
			_leftPane.ColorScheme = _baseColorScheme;
			_rightPane.ColorScheme = _baseColorScheme;
			_top?.SetNeedsDisplay();
		}
		static ColorScheme _baseColorScheme;
		static MenuItem[] CreateColorSchemeMenuItems()
		{
			List<MenuItem> menuItems = new List<MenuItem>();
			foreach (var sc in Colors.ColorSchemes)
			{
				var item = new MenuItem();
				item.Title = $"_{sc.Key}";
				item.Shortcut = Key.AltMask | (Key)sc.Key.Substring(0, 1)[0];
				item.CheckType |= MenuItemCheckStyle.Radio;
				item.Checked = sc.Value == _baseColorScheme;
				item.Action += () => {
					_baseColorScheme = sc.Value;
					SetColorScheme();
					foreach (var menuItem in menuItems)
					{
						menuItem.Checked = menuItem.Title.Equals($"_{sc.Key}") && sc.Value == _baseColorScheme;
					}
				};
				menuItems.Add(item);
			}
			return menuItems.ToArray();
		}
		private void categoryList_SelectedChanged(ListViewItemEventArgs e)
		{
			if (_categoryListViewItem != _categoryListView.SelectedItem)
			{
				_scenarioListViewItem = 0;
			}
			_categoryListViewItem = _categoryListView.SelectedItem;
			var item = _categories[_categoryListView.SelectedItem];
			List<Type> newlist;
			if (item.Equals("All"))
			{
				newlist = _scenarios;
			}
			else
			{
				newlist = _scenarios.Where(t => Scenario.ScenarioCategory.GetCategories(t).Contains(item)).ToList();
			}
			_scenarioListView.Source = new ScenarioListDataSource(newlist);
			_scenarioListView.SelectedItem = _scenarioListViewItem;

		}
		private void _scenarioOpenSelectedItem(EventArgs e)
		{
			if (Scenario is null)
			{
				_scenarioListViewItem = _scenarioListView.SelectedItem;
				var source = _scenarioListView.Source as ScenarioListDataSource;
				Scenario = (Scenario)Activator.CreateInstance(source.Scenarios[_scenarioListView.SelectedItem], Core);
				Application.RequestStop();

			}

		}
		/// <summary>
		/// When Scenarios are running we need to override the behavior of the Menu 
		/// and Statusbar to enable Scenarios that use those (or related key input)
		/// to not be impacted. Same as for tabs.
		/// </summary>
		/// <param name="ke"></param>
		private static void KeyDownHandler(View.KeyEventEventArgs a)
		{
			//if (a.KeyEvent.Key == Key.Tab || a.KeyEvent.Key == Key.BackTab) {
			//	// BUGBUG: Work around Issue #434 by implementing our own TAB navigation
			//	if (_top.MostFocused == _categoryListView)
			//		_top.SetFocus (_rightPane);
			//	else
			//		_top.SetFocus (_leftPane);
			//}

			if (a.KeyEvent.IsCapslock)
			{
				_capslock.Title = "Caps: On";
				_statusBar.SetNeedsDisplay();
			}
			else
			{
				_capslock.Title = "Caps: Off";
				_statusBar.SetNeedsDisplay();
			}

			if (a.KeyEvent.IsNumlock)
			{
				_numlock.Title = "Num: On";
				_statusBar.SetNeedsDisplay();
			}
			else
			{
				_numlock.Title = "Num: Off";
				_statusBar.SetNeedsDisplay();
			}

			if (a.KeyEvent.IsScrolllock)
			{
				_scrolllock.Title = "Scroll: On";
				_statusBar.SetNeedsDisplay();
			}
			else
			{
				_scrolllock.Title = "Scroll: Off";
				_statusBar.SetNeedsDisplay();
			}
		}
		private static void OpenUrl(string url)
		{
			try
			{
				Process.Start(url);
			}
			catch
			{
				// hack because of this: https://github.com/dotnet/corefx/issues/10361
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					url = url.Replace("&", "^&");
					Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					Process.Start("xdg-open", url);
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					Process.Start("open", url);
				}
				else
				{
					throw;
				}
			}
		}
		internal class ScenarioListDataSource : IListDataSource
		{
			private readonly int len;
			public List<Type> Scenarios { get; set; }
			public bool IsMarked(int item) => false;
			public int Count => Scenarios.Count;
			public int Length => len;
			public ScenarioListDataSource(List<Type> itemList)
			{
				Scenarios = itemList;
				len = GetMaxLengthItem();
			}
			public void Render(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width, int start = 0)
			{
				container.Move(col, line);
				// Equivalent to an interpolated string like $"{Scenarios[item].Name, -widtestname}"; if such a thing were possible
				var s = string.Format(string.Format("{{0,{0}}}", -_nameColumnWidth), Display.Scenario.ScenarioMetadata.GetName(Scenarios[item]));
                RenderUstr(driver, $"{s}  {Display.Scenario.ScenarioMetadata.GetDescription(Scenarios[item])}", col, line, width, start);
			}
			public void SetMark(int item, bool value)
			{
			}
			int GetMaxLengthItem()
			{
				if (Scenarios?.Count == 0)
				{
					return 0;
				}

				int maxLength = 0;
				for (int i = 0; i < Scenarios.Count; i++)
				{
					var s = string.Format(string.Format("{{0,{0}}}", -_nameColumnWidth), Display.Scenario.ScenarioMetadata.GetName(Scenarios[i]));
					var sc = $"{s}  {Display.Scenario.ScenarioMetadata.GetDescription(Scenarios[i])}";
					var l = sc.Length;
					if (l > maxLength)
					{
						maxLength = l;
					}
				}

				return maxLength;
			}	
			// A slightly adapted method from: https://github.com/migueldeicaza/gui.cs/blob/fc1faba7452ccbdf49028ac49f0c9f0f42bbae91/Terminal.Gui/Views/ListView.cs#L433-L461
			private void RenderUstr(ConsoleDriver driver, ustring ustr, int col, int line, int width, int start = 0)
			{
				int used = 0;
				int index = start;
				while (index < ustr.Length)
				{
					(var rune, var size) = Utf8.DecodeRune(ustr, index, index - ustr.Length);
					var count = Rune.ColumnWidth(rune);
					if (used + count >= width) break;
					driver.AddRune(rune);
					used += count;
					index += size;
				}

				while (used < width)
				{
					driver.AddRune(' ');
					used++;
				}
			}
			public IList ToList()
			{
				return Scenarios;
			}
		}
	}
}
