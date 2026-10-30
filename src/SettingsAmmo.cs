using System;
using System.Collections.Generic;
using System.Globalization;
using Noesis;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// The Ammunition tab of the settings window: the gun ammunition of the author's pack (ammunition_fix.txt) in a table - the name from
    /// the game, accuracy (circular error probable, m) and warhead power with the game's own values beside them, editable; the arrow resets
    /// a row to the game's values. Edits go through the same work copy as the other settings (Apply / OK / Cancel, the yellow dot).
    /// Keys of the work copy: "Ammo/&lt;ammunition&gt;/0" (accuracy) and "Ammo/&lt;ammunition&gt;/1" (power).
    /// </summary>
    static partial class SettingsWindow
    {
        static bool _ammoFields;

        static bool IsAmmo(string key) { return key != null && key.StartsWith("Ammo/", StringComparison.Ordinal); }

        static void AmmoKey(string key, out string name, out int which)
        {
            int i = key.LastIndexOf('/');
            name = key.Substring(5, i - 5); which = key[i + 1] == '1' ? 1 : 0;
        }

        static string Fmt(float v) { return float.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture); }

        /// <summary>What is applied now: the player's value, else the pack's, else the game's.</summary>
        static string AmmoSavedText(string key)
        {
            string n; int w; AmmoKey(key, out n, out w);
            float v = AwcPlugin.AmmoEffective(n, w);
            if (float.IsNaN(v)) { var g = AwcPlugin.AmmoGameValues(n); v = w == 0 ? g.Cep : g.Power; }
            return float.IsNaN(v) ? "0" : Fmt(v);
        }

        static string AmmoDefaultText(string key)
        {
            string n; int w; AmmoKey(key, out n, out w);
            float v = AwcPlugin.AmmoPack(n, w);
            if (float.IsNaN(v)) { var g = AwcPlugin.AmmoGameValues(n); v = w == 0 ? g.Cep : g.Power; }
            return float.IsNaN(v) ? "0" : Fmt(v);
        }

        /// <summary>"default: pack value" and "game: the game's own value".</summary>
        static string AmmoHint(string name, int w, float game)
        {
            float p = AwcPlugin.AmmoPack(name, w); if (float.IsNaN(p)) p = game;
            string ps = Fmt(p), gs = Fmt(game);
            return Loc.F("set.default", ps.Length == 0 ? "?" : ps) + "\n" + Loc.F("set.gameVal", gs.Length == 0 ? "?" : gs);
        }

        static void EnsureAmmoFields()
        {
            if (_ammoFields) return;
            _ammoFields = true;
            foreach (var n in AwcPlugin.AmmoNames())
            {
                Fields.Add(new Field { Tab = "tabs.guns", Group = "grp.ammoList", Key = "Ammo/" + n + "/0", Label = n, Unit = "", Kind = Kind.Number, Step = 1f, Min = 0f, Max = 100000f });
                Fields.Add(new Field { Tab = "tabs.guns", Group = "grp.ammoList", Key = "Ammo/" + n + "/1", Label = n, Unit = "", Kind = Kind.Number, Step = 0.1f, Min = 0f, Max = 100000f });
            }
        }

        static Field AmmoField(string name, int which)
        {
            string k = "Ammo/" + name + "/" + which;
            foreach (var f in Fields) if (f.Key == k) return f;
            return null;
        }

        /// <summary>Apply: the values that differ from the pack / the game become the player's values.</summary>
        static void AmmoCommit()
        {
            if (!_ammoFields) return;
            bool any = false;
            foreach (var f in Fields) if (IsAmmo(f.Key) && !Same(Get(f), Saved(f))) { any = true; break; }
            if (!any) return;
            var user = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in AwcPlugin.AmmoNames())
            {
                var v = new[] { float.NaN, float.NaN };
                for (int i = 0; i < 2; i++)
                {
                    var f = AmmoField(n, i); if (f == null) continue;
                    float w; if (!TryNum(Get(f), out w)) continue;
                    float b = AwcPlugin.AmmoPack(n, i);
                    if (float.IsNaN(b)) { var g = AwcPlugin.AmmoGameValues(n); b = i == 0 ? g.Cep : g.Power; }
                    if (float.IsNaN(b) || Math.Abs(w - b) > 1e-5f) v[i] = w;
                }
                if (!float.IsNaN(v[0]) || !float.IsNaN(v[1])) user[n] = v;
            }
            AwcPlugin.AmmoSaveUser(user);
        }

        static void AmmoSetAll(Func<string, int, float> value)
        {
            foreach (var n in AwcPlugin.AmmoNames())
                for (int i = 0; i < 2; i++)
                {
                    float v = value(n, i); var f = AmmoField(n, i);
                    if (f != null && !float.IsNaN(v)) _work[f.Key] = Fmt(v);
                }
            Show();
        }

        static float GameValue(string n, int i) { var g = AwcPlugin.AmmoGameValues(n); return i == 0 ? g.Cep : g.Power; }

        static void AmmoTable()
        {
            _body.Children.Add(GroupTitle(Loc.T("grp.ammoList")));
            var note = new TextBlock { Text = Loc.T("ammo.note"), Foreground = B(CDim), FontSize = 11f, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12f, 0f, 12f, 6f) };
            _body.Children.Add(note);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 0f, 12f, 6f) };
            bar.Children.Add(Btn(Loc.T("ammo.resetAll"), () => AmmoSetAll((n, i) => GameValue(n, i))));
            bar.Children.Add(Btn(Loc.T("ammo.restorePack"), () => AmmoSetAll((n, i) => { float p = AwcPlugin.AmmoPack(n, i); return float.IsNaN(p) ? GameValue(n, i) : p; })));
            _body.Children.Add(bar);
            _body.Children.Add(AmmoHeader());
            foreach (var n in AwcPlugin.AmmoNames()) _body.Children.Add(AmmoRow(n));
        }

        static Grid AmmoColumns()
        {
            var g = new Grid { Background = B(Color.FromArgb(0, 0, 0, 0)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star), MinWidth = 200f });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78f) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30f) });
            return g;
        }

        static FrameworkElement AmmoHeader()
        {
            var g = AmmoColumns();
            string[] t = { Loc.T("ammo.colName"), Loc.T("ammo.colCep"), "", Loc.T("ammo.colPower"), "", "" };
            for (int c = 0; c < t.Length; c++)
            {
                if (t[c].Length == 0) continue;
                var tb = Text(t[c], CDim, 11f); Grid.SetColumn(tb, c); if (c == 1 || c == 3) Grid.SetColumnSpan(tb, 2);
                g.Children.Add(tb);
            }
            return new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 2f) };
        }

        static FrameworkElement AmmoRow(string name)
        {
            var fc = AmmoField(name, 0); var fp = AmmoField(name, 1);
            var g = AmmoColumns();
            var outer = new Border { Child = g, Padding = new Thickness(12f, 3f, 6f, 3f), Background = B(Color.FromArgb(0, 0, 0, 0)) };
            var game = AwcPlugin.AmmoGameValues(name);

            var lab = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
            lab.Children.Add(dot);
            lab.Children.Add(Text(game.Title == "?" ? name : game.Title, CText));
            if (game.Title != "?") { var id = Text(name, CDim, 10.5f); id.Margin = new Thickness(8f, 0f, 0f, 0f); lab.Children.Add(id); }
            g.Children.Add(lab);

            Action mark = () =>
            {
                dot.Visibility = Same(Get(fc), Saved(fc)) && Same(Get(fp), Saved(fp)) ? Visibility.Hidden : Visibility.Visible;
                UpdateStatus(); UpdateTabDots();
            };
            Action rc, rp;
            var ec = NumberEditor(fc, mark, out rc); Grid.SetColumn(ec, 1); g.Children.Add(ec);
            var gc = Text(AmmoHint(name, 0, game.Cep), CDim, 11f); gc.Margin = new Thickness(8f, 0f, 0f, 0f); Grid.SetColumn(gc, 2); g.Children.Add(gc);
            var ep = NumberEditor(fp, mark, out rp); Grid.SetColumn(ep, 3); g.Children.Add(ep);
            var gp = Text(AmmoHint(name, 1, game.Power), CDim, 11f); gp.Margin = new Thickness(8f, 0f, 0f, 0f); Grid.SetColumn(gp, 4); g.Children.Add(gp);

            var rst = new Button { Content = "↺", Width = 24f, Height = 22f, Padding = new Thickness(0f), HorizontalAlignment = HorizontalAlignment.Right };
            try { ToolTipService.SetToolTip(rst, Loc.T("set.resetTip")); } catch { }
            rst.Click += (o, e) =>
            {
                _work[fc.Key] = AmmoDefaultText(fc.Key);
                _work[fp.Key] = AmmoDefaultText(fp.Key);
                rc(); rp(); mark();
            };
            Grid.SetColumn(rst, 5); g.Children.Add(rst);

            mark();
            outer.MouseEnter += (o, e) =>
            {
                outer.Background = B(CHl);
                _help.Inlines.Clear();
                var r = new Run(game.Title == "?" ? name : game.Title + " (" + name + ")"); r.FontWeight = FontWeight.Bold; r.Foreground = B(CText);
                _help.Inlines.Add(r);
                _help.Inlines.Add(new Run(" - " + Loc.T("ammo.note")));
            };
            outer.MouseLeave += (o, e) => outer.Background = B(Color.FromArgb(0, 0, 0, 0));
            return outer;
        }
    }
}
