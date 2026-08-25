// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — built-in Reverb body (kind 2), mockup 2h on the shared shell
// (700×260): a LIVE strip (Decay · HF Damp · Freeze · Algorithm · Mix) over a body of
// the decay-tail GRAPH (flex) | two honest bands SPACE (pre-delay/size/diffusion) and
// TONE (low cut/high cut/width) + MOD | an OUTPUT rail (dry/wet · out). All controls are
// generic device params on the shared gauge Knob.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class ReverbDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match engine layout) ─────────────────────────
    private const int Decay = 0;
    private const int HFDamp = 1;
    private const int PreDelay = 2;
    private const int Size = 3;
    private const int Diffusion = 4;
    private const int LowCut = 5;
    private const int HighCut = 6;
    private const int StereoWidth = 7;
    private const int ModRate = 8;
    private const int ModDepth = 9;
    private const int Algorithm = 10;
    private const int Freeze = 11;
    private const int DryWet = 12;
    private const int Output = 13;

    // ── Display ranges ───────────────────────────────────────────────────────
    private const double DecayMinSec = 0.2, DecayMaxSec = 12.0;
    private const double FreqMinHz = 20, FreqMaxHz = 20000;
    private const double RateMinHz = 0.05, RateMaxHz = 5.0;
    private const double PreDelayMaxMs = 200;

    // ── Device-local palette ─────────────────────────────────────────────────
    private static readonly IBrush LiveStripBg = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush RailBg = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush SliderHandleBg = new SolidColorBrush(Color.Parse("#A39D8F"));

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;

        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void BeginWrite(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void EndWrite(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");

        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

        // ── Tail visualization ───────────────────────────────────────────────
        var tail = new ReverbTail(engine, track, di, Decay, PreDelay, v => Exp(v, DecayMinSec, DecayMaxSec))
        {
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ctx.AddDeviceRefresher(tail.Tick);

        // ── Value formatters ─────────────────────────────────────────────────
        string Sec(double v) => $"{Exp(v, DecayMinSec, DecayMaxSec):0.00} s";
        string Pct(double v) => $"{v * 100:0}%";
        string Hz(double v)
        {
            double f = Exp(v, FreqMinHz, FreqMaxHz);
            return f >= 1000 ? $"{f / 1000:0.0}k" : $"{f:0}";
        }
        string PreMs(double v) => $"{v * PreDelayMaxMs:0} ms";
        string RateF(double v) => $"{Exp(v, RateMinHz, RateMaxHz):0.00} Hz";
        string DbF(double v) => v <= 0.001 ? "−∞" : $"{20 * Math.Log10(v * 2):+0.0;-0.0;0.0}";

        // ── Widget factories ─────────────────────────────────────────────────

        Control Cell(string name, int p, Func<double, string> fmt, IBrush? arc = null, double size = 34)
        {
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 8, Foreground = TextPrimary };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");

            var knob = new Knob(P(p), 1.0)
            {
                Accent = true,
                ArcColor = arc,
                Default = engine.DeviceParamDefault(track, di, p),
                Width = size,
                Height = size,
            };
            knob.ValueChanged += v =>
            {
                SetP(p, (float)v);
                val.Text = fmt(v);
                tail.Tick();
            };
            knob.GestureBegin += () => BeginWrite(p);
            knob.GestureEnd += () => EndWrite(p);

            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), name);

            ctx.AddDeviceRefresher(() =>
            {
                if (knob.Dragging) return;
                float c = P(p);
                if (Math.Abs(c - knob.Value) > 1e-3)
                {
                    knob.Value = c;
                    val.Text = fmt(c);
                }
            });

            return KnobCell(name, knob, val, size + 16);
        }

        Control Row(params Control[] cs)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            foreach (var c in cs) sp.Children.Add(c);
            return sp;
        }

        TextBlock BandHeader(string title) => new()
        {
            Text = title,
            FontSize = 8,
            FontWeight = FontWeight.Bold,
            Foreground = TextTertiary,
            Margin = new Thickness(0, 0, 0, 3),
        };

        Control Band(string title, params Control[] knobs) => new Border
        {
            Background = Card2,
            BorderBrush = BorderDef,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 5),
            Child = new StackPanel { Children = { BandHeader(title), Row(knobs) } },
        };

        // Vertical band (knobs stacked) — used for MOD so it sits as a slim island
        // to the right of the graph instead of a third row that overruns the card.
        Control VBand(string title, params Control[] knobs)
        {
            var col = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            
            foreach (var k in knobs) col.Children.Add(k);

            var header = BandHeader(title);
            var child = col;
            var grid = new Grid()
            {
                RowDefinitions = new RowDefinitions("Auto, *"),
                RowSpacing = 6,
                Children = { header, child }
            };
            
            Grid.SetRow(header, 0);
            Grid.SetRow(child, 1);

            return new Border
            {
                Background = Card2,
                BorderBrush = BorderDef,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 5),
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = grid,
            };
        }

        // Compact horizontal slider for the LIVE strip.
        Control Slider(int p, string name, Func<double, string> fmt, double w)
        {
            var bg = new Border { Width = w, Height = 3, Background = Sunken, CornerRadius = new CornerRadius(2) };
            var fill = new Border { Height = 3, Background = Brass, CornerRadius = new CornerRadius(2) };
            var handle = new Border { Width = 8, Height = 9, Background = SliderHandleBg, CornerRadius = new CornerRadius(2) };

            var canvas = new Canvas
            {
                Width = w,
                Height = 9,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Canvas.SetTop(bg, 3);
            Canvas.SetTop(fill, 3);
            Canvas.SetTop(handle, 0);
            canvas.Children.Add(bg);
            canvas.Children.Add(fill);
            canvas.Children.Add(handle);

            var val = new TextBlock
            {
                FontSize = 9,
                Foreground = TextPrimary,
                Width = 46,
                VerticalAlignment = VerticalAlignment.Center,
            };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");

            bool drag = false;

            void UpdateVisual(double v)
            {
                fill.Width = Math.Max(0, v * w);
                Canvas.SetLeft(handle, v * w - handle.Width / 2);
                val.Text = fmt(v);
            }

            void ApplyPointer(PointerEventArgs e)
            {
                double v = Math.Clamp(e.GetPosition(canvas).X / w, 0, 1);
                SetP(p, (float)v);
                UpdateVisual(v);
                tail.Tick();
            }

            void EndDrag()
            {
                if (!drag) return;
                drag = false;
                EndWrite(p);
            }

            canvas.PointerPressed += (_, e) =>
            {
                drag = true;
                BeginWrite(p);
                e.Pointer.Capture(canvas);
                ApplyPointer(e);
            };
            canvas.PointerMoved += (_, e) => { if (drag) ApplyPointer(e); };
            canvas.PointerReleased += (_, e) => { EndDrag(); e.Pointer.Capture(null); };
            canvas.PointerCaptureLost += (_, _) => EndDrag(); // don't leave a dangling automation session

            ctx.AddDeviceRefresher(() => { if (!drag) UpdateVisual(P(p)); });
            UpdateVisual(P(p));

            var root = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = name,
                        FontSize = 8,
                        FontWeight = FontWeight.Bold,
                        Foreground = TextTertiary,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    canvas,
                    val,
                },
            };
            MidiLearn.Bind(root, MidiTarget.DeviceParam(track, di, p), name);
            return root;
        }

        // Discrete selector rendered as a chip row (Algorithm).
        Control Chips(int p, string[] names)
        {
            int n = names.Length;
            var chips = new Border[n];

            void SyncVisual()
            {
                int cur = Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);
                for (int i = 0; i < n; i++)
                {
                    bool on = i == cur;
                    chips[i].Background = on ? AccentSubtleB : Brushes.Transparent;
                    ((TextBlock)chips[i].Child!).Foreground = on ? AccentBright : TextTertiary;
                }
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = names[i], FontSize = 9, Foreground = TextTertiary },
                };
                chip.PointerPressed += (_, _) =>
                {
                    BeginWrite(p);
                    SetP(p, iv / (float)(n - 1));
                    EndWrite(p);
                    SyncVisual();
                };
                chips[i] = chip;
                row.Children.Add(chip);
            }

            ctx.AddDeviceRefresher(SyncVisual);
            SyncVisual();

            var host = new Border
            {
                Background = Sunken,
                BorderBrush = BorderDef,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = row,
            };
            MidiLearn.Bind(host, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return host;
        }

        Control Toggle(int p, string label)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold },
            };

            void SyncVisual()
            {
                bool on = P(p) > 0.5f;
                b.Background = on ? AccentSubtleB : Sunken;
                b.BorderBrush = on ? Brass : BorderDef;
                ((TextBlock)b.Child!).Foreground = on ? AccentBright : TextTertiary;
            }

            b.PointerPressed += (_, _) =>
            {
                BeginWrite(p);
                SetP(p, P(p) > 0.5f ? 0f : 1f);
                EndWrite(p);
                SyncVisual();
            };

            ctx.AddDeviceRefresher(SyncVisual);
            SyncVisual();
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), label);
            return b;
        }

        // ── Layout ───────────────────────────────────────────────────────────

        // Top LIVE strip: performance-critical params, always visible.
        var live = new Border
        {
            Height = 34,
            Background = LiveStripBg,
            BorderBrush = BorderDef,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0),
                Children =
                {
                    Slider(Decay, "DECAY", Sec, 80),
                    Slider(HFDamp, "HF DAMP", Pct, 56),
                    Toggle(Freeze, "Freeze"),
                    Chips(Algorithm, new[] { "Hall", "Room", "Plate", "Chamber" }),
                    Slider(DryWet, "MIX", Pct, 56),
                },
            },
        };

        // Middle: tail graph | slim MOD island | SPACE+TONE column.
        var spaceBand = Band("SPACE", Cell("Pre", PreDelay, PreMs), Cell("Size", Size, Pct), Cell("Diffuse", Diffusion, Pct));
        var toneBand = Band("TONE", Cell("Low Cut", LowCut, Hz), Cell("High Cut", HighCut, Hz),
            Cell("Width", StereoWidth, Pct));
        var bands = new Grid()
        {
            RowSpacing = 6,
            RowDefinitions = new RowDefinitions("*,*"),
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                spaceBand,
                toneBand,
            },
        };
        
        Grid.SetRow(spaceBand, 0);
        Grid.SetRow(toneBand, 1);
        
        var mod = VBand("MOD", Cell("Rate", ModRate, RateF, Teal), Cell("Depth", ModDepth, Pct, Teal));

        // Right rail: output stage.
        var rail = new Border
        {
            Width = 96,
            Background = RailBg,
            BorderBrush = BorderDef,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(6, 8),
            Child = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Cell("Dry/Wet", DryWet, Pct, Teal, 40),
                    Cell("Out", Output, DbF, null, 38),
                },
            },
        };
        DockPanel.SetDock(rail, Dock.Right);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(9, 8),
        };
        content.Children.Add(tail);
        Grid.SetColumn(mod, 1);
        content.Children.Add(mod);
        Grid.SetColumn(bands, 2);
        content.Children.Add(bands);

        var contentDock = new DockPanel { LastChildFill = true, Children = { rail, content } };

        DockPanel.SetDock(live, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { live, contentDock } };
    }
}
