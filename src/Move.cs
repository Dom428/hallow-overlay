// Hallow Overlay - the panel's own window: click-through, never takes focus, never in the taskbar or Alt+Tab, placed on the
// screen and spot from the settings (1.15). "Move it" mode makes it draggable for a while: an outline, snapping to the 9 spots,
// the screen's edges and centre lines (hold Shift or Alt to place freely), arrow keys nudge, Enter / Esc / Done end it.
// Leaving move mode (also on focus loss and after 2 minutes without a drag or key) always makes it click-through again.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HallowOverlay
{
    public class OverlayWindow : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;
        public const int WM_ACTIVATE = 0x0006, WM_MOVING = 0x0216, WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
        const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

        IntPtr hwnd; UIElement panel; MoveHint hint; DispatcherTimer moveTimer; bool inDrag;
        public Func<List<ScreenInfo>> ScreensSource = Monitors.All;   // tests put made-up screens here
        public bool Moving { get; private set; }
        public OverlaySettings MoveTarget { get; private set; }        // the look the dragged spot is saved into
        public TimeSpan MoveTimeout = TimeSpan.FromMinutes(2);
        public Action Moved;            // a drag or nudge was saved into MoveTarget
        public Action<string> Ended;    // move mode ended: "done", "focus", "timeout", "off"
        public Rect LastRect; public ScreenInfo LastScreen; public bool LastScreenFound = true; public string LastGuide = "";
        public double SnapDip = 16;

        public OverlayWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; Focusable = false; FontFamily = new FontFamily("Segoe UI"); IsHitTestVisible = false;
            SourceInitialized += (s, e) => { hwnd = new WindowInteropHelper(this).Handle; SetClickThrough(!Moving); HwndSource.FromHwnd(hwnd).AddHook(Hook); };   // 1.16: a window first created in move mode isn't click-through
            PreviewKeyDown += OnKey;
        }
        public IntPtr Handle { get { return hwnd; } }

        // ---- dragging: the window follows the pointer from where the press started (snapping unless Shift / Alt, always on a screen)
        bool dragging; Point dragFrom; Rect dragStart;
        public bool Dragging { get { return dragging; } }
        public bool BeginDrag(Point screenPt)
        {
            if (!Moving) return false;
            var r = WindowRect(); if (r.IsEmpty) return false;
            dragging = true; inDrag = true; dragFrom = screenPt; dragStart = r; Poke(); return true;
        }
        public void DragTo(Point screenPt)
        {
            if (!dragging) return;
            var r = new Rect(dragStart.X + screenPt.X - dragFrom.X, dragStart.Y + screenPt.Y - dragFrom.Y, dragStart.Width, dragStart.Height);
            r = Adjust(r, !NoSnap()); MoveTo(r);
        }
        public void EndDrag()
        {
            if (!dragging) return;
            dragging = false; inDrag = false; Commit(); Poke();
        }

        // ---- click-through
        void SetClickThrough(bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, -20) | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            ex = on ? ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE : ex & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            SetWindowLong(hwnd, -20, ex);
        }
        public bool IsClickThrough { get { if (hwnd == IntPtr.Zero) return !Moving; int ex = GetWindowLong(hwnd, -20); return (ex & WS_EX_TRANSPARENT) != 0 && (ex & WS_EX_NOACTIVATE) != 0; } }
        // Called every refresh: outside move mode the window is click-through, whatever happened.
        public void EnsureClickThrough() { if (!Moving && !IsClickThrough) { SetClickThrough(true); IsHitTestVisible = false; } else if (Moving && hwnd != IntPtr.Zero && IsClickThrough) SetClickThrough(false); }

        // ---- content
        public void SetPanel(UIElement content)
        {
            panel = content; ShowPanel();
        }
        // the panel as the window's content: framed in move mode, plain otherwise
        void ShowPanel()
        {
            Content = null;
            var fe = panel as FrameworkElement; var p = fe == null ? null : fe.Parent as System.Windows.Controls.Panel; if (p != null) p.Children.Remove(panel);
            Content = Moving ? Framed(panel, IsEmpty(panel)) : panel;
        }
        static bool IsEmpty(UIElement e) { var b = e as Border; return e == null || (b != null && b.Child == null); }
        // The panel with a dashed outline over it (move mode and the --snap picture).
        public static FrameworkElement Framed(UIElement content, bool empty)
        {
            var acc = Accent.Brush().Color;
            var g = new Grid { MinWidth = 150, MinHeight = 36, Cursor = Cursors.SizeAll };
            if (content != null && !empty) g.Children.Add(content);
            else g.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(200, 0x16, 0x12, 0x1C)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8),
                Child = new TextBlock { Text = "Hallow Overlay", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            g.Children.Add(new Rectangle { Stroke = new SolidColorBrush(acc), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 2.5 }, RadiusX = 10, RadiusY = 10, Fill = new SolidColorBrush(Color.FromArgb(46, acc.R, acc.G, acc.B)) });
            return g;
        }

        // ---- placing
        double DeviceScale()
        {
            var src = PresentationSource.FromVisual(this);
            return src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : Monitors.SystemScale();
        }
        public Rect WindowRect() { RECT r; if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out r)) return Rect.Empty; return new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top); }
        void MoveTo(Rect r)
        {
            int x = (int)Math.Round(r.X), y = (int)Math.Round(r.Y);
            var now = WindowRect(); if (!now.IsEmpty && (int)now.X == x && (int)now.Y == y) return;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        // Put the panel where the look says (skipped while it's being dragged).
        public void Place(OverlaySettings p)
        {
            if (hwnd == IntPtr.Zero || inDrag) return;
            UpdateLayout();
            var list = ScreensSource(); bool found; var s = Placement.Pick(list, p.ScreenId, p.Screen, out found);
            double k = DeviceScale(), w = Math.Round(ActualWidth * k), h = Math.Round(ActualHeight * k);
            var r = Placement.Compute(p.Anchor, p.Margin, p.OffX, p.OffY, s, w, h);
            r = new Rect(Math.Round(r.X), Math.Round(r.Y), w, h);
            MoveTo(r); LastRect = r; LastScreen = s; LastScreenFound = found;
            if (Moving) PlaceHint(r, s);
        }

        // While the panel is hidden: where it would go (for status.json), from its last size - nothing moves.
        public void Predict(OverlaySettings p)
        {
            if (hwnd == IntPtr.Zero || ActualWidth <= 0) return;
            var list = ScreensSource(); bool found; var s = Placement.Pick(list, p.ScreenId, p.Screen, out found);
            double k = DeviceScale(), w = Math.Round(ActualWidth * k), h = Math.Round(ActualHeight * k);
            var r = Placement.Compute(p.Anchor, p.Margin, p.OffX, p.OffY, s, w, h);
            LastRect = new Rect(Math.Round(r.X), Math.Round(r.Y), w, h); LastScreen = s; LastScreenFound = found;
        }

        // ---- move mode
        public void BeginMove(OverlaySettings target, bool activate)
        {
            MoveTarget = target;
            if (!Moving)
            {
                Moving = true; LastGuide = "";
                new WindowInteropHelper(this).EnsureHandle();   // 1.15 bug: before the panel was ever shown there was no window yet, so the styles stayed click-through
                SetClickThrough(false); IsHitTestVisible = true; Focusable = true;
                ShowPanel();
                if (hint == null) { hint = new MoveHint(); hint.Done = () => EndMove("done"); hint.Owner2 = this; }
                if (!hint.IsVisible) hint.Show();
                UpdateLayout(); var r = WindowRect(); if (!r.IsEmpty) PlaceHint(r, Placement.At(ScreensSource(), Center(r)));
                if (moveTimer == null) { moveTimer = new DispatcherTimer(); moveTimer.Tick += (s, e) => EndMove("timeout"); }
            }
            Poke();
            if (activate) { try { Activate(); Focus(); Keyboard.Focus(this); } catch { } }
        }
        void Poke() { if (moveTimer == null) return; moveTimer.Stop(); moveTimer.Interval = MoveTimeout; if (Moving) moveTimer.Start(); }
        public void EndMove(string why)
        {
            bool was = Moving; Moving = false; inDrag = false; if (dragging) { dragging = false; ReleaseCapture(); }
            if (moveTimer != null) moveTimer.Stop();
            if (hint != null && hint.IsVisible) hint.Hide();
            ShowPanel();
            IsHitTestVisible = false; Focusable = false; SetClickThrough(true);
            if (was && Ended != null) Ended(why);
        }
        static Point Center(Rect r) { return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
        static bool NoSnap() { return GetKeyState(0x10) < 0 || GetKeyState(0x12) < 0; }   // Shift or Alt held

        // While dragging: snap (unless Shift / Alt), keep it fully on the screen it's over.
        public Rect Adjust(Rect r, bool snap)
        {
            var list = ScreensSource(); var s = Placement.At(list, Center(r)); string gx = "", gy = "";
            if (snap) r = Placement.SnapRect(r, s, MoveTarget == null ? Placement.DefaultMargin : MoveTarget.Margin, SnapDip * s.Scale, out gx, out gy);
            r = Placement.Clamp(r, s.Bounds);
            LastGuide = (gx.Length > 0 && gy.Length > 0 ? gy + " / " + gx : gx + gy);
            PlaceHint(r, s); return r;
        }
        // Save where it is now into the target look (spot + offset as a fraction of the screen + the screen).
        public void Commit()
        {
            var r = WindowRect(); if (r.IsEmpty || MoveTarget == null) return;
            var s = Placement.At(ScreensSource(), Center(r));
            r = Placement.Clamp(r, s.Bounds); MoveTo(r);
            string a; double ox, oy; Placement.FromRect(r, s, MoveTarget.Margin, out a, out ox, out oy);
            MoveTarget.Anchor = a; MoveTarget.Corner = Placement.CornerOf(a); MoveTarget.OffX = ox; MoveTarget.OffY = oy;
            MoveTarget.Screen = s.Primary ? "" : s.Device; MoveTarget.ScreenId = s.Primary ? "" : s.Id; MoveTarget.ScreenName = s.Primary ? "" : s.Name;
            LastRect = r; LastScreen = s; LastScreenFound = true; PlaceHint(r, s);
            if (Moved != null) Moved();
        }
        public void Nudge(int dx, int dy)
        {
            if (!Moving) return; Poke();
            var r = WindowRect(); if (r.IsEmpty) return;
            r.Offset(dx, dy); r = Placement.Clamp(r, Placement.At(ScreensSource(), Center(r)).Bounds);
            MoveTo(r); Commit();
        }
        void OnKey(object sender, KeyEventArgs e)
        {
            if (!Moving) return;
            var k = e.Key == Key.System ? e.SystemKey : e.Key; int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            if (k == Key.Enter || k == Key.Escape) { EndMove("done"); e.Handled = true; }
            else if (k == Key.Left) { Nudge(-step, 0); e.Handled = true; }
            else if (k == Key.Right) { Nudge(step, 0); e.Handled = true; }
            else if (k == Key.Up) { Nudge(0, -step); e.Handled = true; }
            else if (k == Key.Down) { Nudge(0, step); e.Handled = true; }
        }
        [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr h);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        // 1.16: the drag runs on the window's own mouse messages (button down, moves with the mouse captured, button up),
        // not WPF's DragMove. The screen point is where the window is now + the message's client point.
        public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_CAPTURECHANGED = 0x0215;
        public static Point ScreenPoint(Rect win, IntPtr lp) { long v = lp.ToInt64(); return new Point(win.X + (short)(v & 0xFFFF), win.Y + (short)((v >> 16) & 0xFFFF)); }
        // mouse messages from this window or the hint card; true = used
        public bool MouseMessage(IntPtr from, Rect fromRect, int msg, IntPtr lp)
        {
            switch (msg)
            {
                case WM_LBUTTONDOWN: if (BeginDrag(ScreenPoint(fromRect, lp))) { SetCapture(from); return true; } return false;
                case WM_MOUSEMOVE: if (!dragging) return false; DragTo(ScreenPoint(fromRect, lp)); return true;
                case WM_LBUTTONUP: if (!dragging) return false; DragTo(ScreenPoint(fromRect, lp)); EndDrag(); ReleaseCapture(); return true;
                case WM_CAPTURECHANGED: if (dragging && lp != from) EndDrag(); return false;
            }
            return false;
        }
        IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (!Moving) return IntPtr.Zero;
            if (msg >= WM_MOUSEMOVE && msg <= WM_LBUTTONUP || msg == WM_CAPTURECHANGED) { if (MouseMessage(h, WindowRect(), msg, lp)) { handled = true; return IntPtr.Zero; } }
            switch (msg)
            {
                case WM_ENTERSIZEMOVE: inDrag = true; Poke(); break;
                case WM_MOVING:
                    {
                        var rc = (RECT)Marshal.PtrToStructure(lp, typeof(RECT));
                        var r = Adjust(new Rect(rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top), !NoSnap());
                        rc.left = (int)Math.Round(r.X); rc.top = (int)Math.Round(r.Y); rc.right = rc.left + (int)Math.Round(r.Width); rc.bottom = rc.top + (int)Math.Round(r.Height);
                        Marshal.StructureToPtr(rc, lp, false); handled = true; return new IntPtr(1);
                    }
                case WM_EXITSIZEMOVE: inDrag = false; Commit(); Poke(); break;
                case WM_ACTIVATE:
                    if ((wp.ToInt64() & 0xFFFF) == 0) Dispatcher.BeginInvoke(new Action(() => { if (Moving) EndMove("focus"); }));
                    break;
            }
            return IntPtr.Zero;
        }
        void PlaceHint(Rect r, ScreenInfo s)
        {
            if (hint == null || !hint.IsVisible) return;
            hint.SetGuide(LastGuide);
            hint.UpdateLayout(); var hr = hint.WindowRect(); if (hr.IsEmpty) return;
            double gap = 8 * s.Scale, y = r.Bottom + gap;
            if (y + hr.Height > s.Bounds.Bottom) y = r.Top - gap - hr.Height;
            if (y < s.Bounds.Top) y = r.Top + gap;   // a panel as tall as the screen: over it
            var x = Math.Max(s.Bounds.Left, Math.Min(r.X, s.Bounds.Right - hr.Width));
            hint.MoveTo((int)Math.Round(x), (int)Math.Round(y));
        }
        public MoveHint Hint { get { return hint; } }
    }

    // The small card next to the panel in move mode: what to do, and Done. Clicking it never takes the focus from the panel.
    public class MoveHint : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out OverlayWindow.RECT r);
        public Action Done; IntPtr hwnd; TextBlock guide; public Button DoneButton; public OverlayWindow Owner2;
        public MoveHint()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; FontFamily = new FontFamily("Segoe UI"); Left = -32000; Top = -32000;
            Content = Card(() => { if (Done != null) Done(); }, out guide, out DoneButton);
            SourceInitialized += (s, e) => { hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | OverlayWindow.WS_EX_NOACTIVATE | OverlayWindow.WS_EX_TOOLWINDOW); };
            // "Drag me" on the card: dragging the card (anywhere but Done) moves the panel too (1.16)
            Cursor = Cursors.SizeAll;
            SourceInitialized += (s, e) => HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (Owner2 == null || !Owner2.Moving) return IntPtr.Zero;
                if (msg == OverlayWindow.WM_LBUTTONDOWN && OverDone(OverlayWindow.ScreenPoint(WindowRect(), lp))) return IntPtr.Zero;   // Done stays a button
                if ((msg >= OverlayWindow.WM_MOUSEMOVE && msg <= OverlayWindow.WM_LBUTTONUP || msg == OverlayWindow.WM_CAPTURECHANGED) && Owner2.MouseMessage(h, WindowRect(), msg, lp)) handled = true;
                return IntPtr.Zero;
            });
        }
        bool OverDone(Point screen)
        {
            try { var tl = DoneButton.PointToScreen(new Point(0, 0)); var br = DoneButton.PointToScreen(new Point(DoneButton.ActualWidth, DoneButton.ActualHeight)); return new Rect(tl, br).Contains(screen); } catch { return false; }
        }
        public static FrameworkElement Card(Action done, out TextBlock guide, out Button doneBtn)
        {
            var acc = Accent.Brush();
            var dp = new DockPanel();
            doneBtn = new Button { Cursor = Cursors.Hand, Content = "Done", MinWidth = 70, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 4, 12, 4) };
            var st = Application.Current == null ? null : Application.Current.TryFindResource("Accent") as Style; if (st != null) doneBtn.Style = st;
            doneBtn.Click += (s, e) => done(); DockPanel.SetDock(doneBtn, Dock.Right); dp.Children.Add(doneBtn);
            var ic = Icons.Make("expand", 16, acc); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(ic, Dock.Left); dp.Children.Add(ic);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(new TextBlock { Text = "Drag me - Enter/Esc or click Done", FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brushes.White });
            tx.Children.Add(new TextBlock { Text = "Snaps to the 9 spots, the edges and the centre lines (hold Shift or Alt to place freely). Arrow keys: 1 px, Shift: 10 px.", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB0, 0xC8)), TextWrapping = TextWrapping.Wrap, MaxWidth = 330 });
            guide = new TextBlock { FontSize = 11, Foreground = acc, Margin = new Thickness(0, 2, 0, 0) }; tx.Children.Add(guide);
            dp.Children.Add(tx);
            return new Border { Background = new SolidColorBrush(Color.FromArgb(240, 0x24, 0x1E, 0x2E)), BorderBrush = acc, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Child = dp };
        }
        public void SetGuide(string g) { guide.Text = string.IsNullOrEmpty(g) ? "" : "Snapped: " + g; guide.Visibility = string.IsNullOrEmpty(g) ? Visibility.Collapsed : Visibility.Visible; }
        public Rect WindowRect() { OverlayWindow.RECT r; if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out r)) return Rect.Empty; return new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top); }
        public IntPtr Handle2 { get { return hwnd; } }
        public void MoveTo(int x, int y) { if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x1 | 0x4 | 0x10); }
    }
}
