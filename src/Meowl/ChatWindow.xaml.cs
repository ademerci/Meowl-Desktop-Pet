using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Anthropic.Exceptions;
using Meowl.Services;

namespace Meowl;

/// <summary>Die große Sprechblase zum Chatten, schwebt über dem Haustier.</summary>
public partial class ChatWindow : Window
{
    static readonly Brush PetBubble = Frozen(Color.FromRgb(0xF3, 0xEB, 0xE0));
    static readonly Brush UserBubble = Frozen(Color.FromRgb(0xC2, 0x7F, 0x3F));
    static readonly Brush ErrorBubble = Frozen(Color.FromRgb(0xFB, 0xE4, 0xE1));
    static readonly Brush Ink = Frozen(Color.FromRgb(0x3B, 0x2A, 0x1E));
    static readonly Brush Muted = Frozen(Color.FromRgb(0x9A, 0x85, 0x72));
    static readonly Brush LinkBrush = Frozen(Color.FromRgb(0x9A, 0x55, 0x1C));

    // **fett**, [Text](https://…) und nackte Links
    static readonly Regex InlineFormat = new(
        @"\*\*(?<bold>.+?)\*\*|\[(?<label>[^\]]+)\]\((?<href>https?://[^\s)]+)\)|(?<url>https?://[^\s)\]]+[^\s)\].,;:!?])",
        RegexOptions.Compiled);

    readonly PetWindow _pet;
    readonly PetAgent _agent;
    readonly PetSettings _settings;
    readonly Storyboard _typingAnimation;
    CancellationTokenSource? _cts;

    public bool AllowClose { get; set; }
    public event EventHandler? SettingsRequested;

    public ChatWindow(PetWindow pet, PetAgent agent, PetSettings settings)
    {
        _pet = pet;
        _agent = agent;
        _settings = settings;
        InitializeComponent();

        _typingAnimation = CreateTypingAnimation();
        _pet.Moved += (_, _) => { if (IsVisible) Reposition(); };
        _agent.Activity += AddActivity;
        SourceInitialized += (_, _) => NativeMethods.HideFromAltTab(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    public void Open()
    {
        PetNameText.Text = _settings.PetName;
        _pet.ChatOpen = true;
        _pet.HideBubble();
        Show();
        Reposition();
        Activate();
        Input.Focus();

        if (Messages.Children.Count == 0)
        {
            var name = string.IsNullOrWhiteSpace(_settings.UserName) ? "" : $" {_settings.UserName.Trim()}";
            AddMessage(false, _settings.HasApiKey
                ? $"Miau-huu, hallo{name}! 🦉 Ich bin {_settings.PetName}. Frag mich was, lass mich etwas im Web suchen oder sag mir, woran ich dich erinnern soll."
                : $"Miau-huu, hallo{name}! Ich bin {_settings.PetName}. Damit ich antworten kann, brauche ich einen **Claude API-Schlüssel**. Klick oben auf das Zahnrad ⚙.");
        }
    }

    public void CloseChat()
    {
        Hide();
        _pet.ChatOpen = false;
    }

    public void Toggle()
    {
        if (IsVisible) CloseChat();
        else Open();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            CloseChat();
        }
        base.OnClosing(e);
    }

    /// <summary>Über dem Kopf von Meowl platzieren, Spitze zeigt auf ihn.</summary>
    void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        var head = _pet.PetHeadRect;
        var centerX = head.Left + head.Width / 2;

        Height = Math.Clamp(head.Top - wa.Top + 8, 320, 540);
        var left = Math.Clamp(centerX - Width / 2, wa.Left + 2, wa.Right - Width - 2);
        var top = Math.Max(wa.Top, head.Top - Height + 8);
        Left = left;
        Top = top;

        // 14 = Außenrand des Grids, 11 = halbe Breite der Spitze
        var tipX = centerX - left - 14 - 11;
        Canvas.SetLeft(Tip, Math.Clamp(tipX, 18, Width - 28 - 22 - 18));
    }

    // ------------------------------------------------------------------
    //  Senden
    // ------------------------------------------------------------------

    async void Send()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        var text = Input.Text.Trim();
        if (text.Length == 0) return;
        Input.Clear();
        AddMessage(true, text);

        if (!_settings.HasApiKey)
        {
            AddMessage(false, "Ich brauche zuerst einen **Claude API-Schlüssel**, sonst kann ich nicht denken. 🙈 Klick oben auf das Zahnrad ⚙.");
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var reply = await _agent.AskAsync(text, _settings, _cts.Token);
            AddMessage(false, reply);
            _pet.Talk(Math.Min(4, 0.8 + reply.Length * 0.025));
        }
        catch (OperationCanceledException)
        {
            AddActivity("⏹ Abgebrochen");
        }
        catch (MissingApiKeyException)
        {
            AddError("Kein API-Schlüssel hinterlegt. Öffne die Einstellungen (⚙).");
        }
        catch (AnthropicUnauthorizedException)
        {
            AddError("Der API-Schlüssel ist ungültig. Prüfe ihn in den Einstellungen (⚙).");
        }
        catch (AnthropicRateLimitException)
        {
            AddError("Zu viele Anfragen auf einmal. Warte kurz und versuch es nochmal.");
        }
        catch (Anthropic5xxException)
        {
            AddError("Claude ist gerade überlastet. Versuch es gleich nochmal.");
        }
        catch (AnthropicApiException ex)
        {
            AddError(ex.Message.Contains("credit", StringComparison.OrdinalIgnoreCase)
                ? "Dein API-Guthaben ist aufgebraucht. Lade es unter console.anthropic.com auf."
                : $"Fehler von der API: {ex.Message}");
        }
        catch (AnthropicIOException)
        {
            AddError("Keine Verbindung zum Internet oder zur API.");
        }
        catch (Exception ex)
        {
            AddError($"Unerwarteter Fehler: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _pet.SetThinking(busy);
        StatusText.Text = busy ? "denkt nach…" : "bereit";
        SendButton.Content = busy ? "" : "";
        SendButton.ToolTip = busy ? "Abbrechen" : "Senden (Enter)";
        Typing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) _typingAnimation.Begin(this, true);
        else _typingAnimation.Stop(this);
        ScrollToEnd();
    }

    // ------------------------------------------------------------------
    //  Nachrichten anzeigen
    // ------------------------------------------------------------------

    void AddMessage(bool fromUser, string text)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = fromUser ? Brushes.White : Ink,
            LineHeight = 19,
        };
        FillInlines(block.Inlines, text, fromUser);

        var bubble = new Border
        {
            Child = block,
            Background = fromUser ? UserBubble : PetBubble,
            CornerRadius = fromUser ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4),
            Padding = new Thickness(12, 8, 12, 9),
            Margin = fromUser ? new Thickness(46, 4, 0, 4) : new Thickness(0, 4, 30, 4),
            HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            ContextMenu = CopyMenu(text),
        };
        Messages.Children.Add(bubble);
        if (!fromUser)
            StatusText.Text = "bereit";
        ScrollToEnd();
    }

    void AddActivity(string text)
    {
        Messages.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Muted,
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(6, 2, 0, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        StatusText.Text = text;
        ScrollToEnd();
    }

    void AddError(string text)
    {
        Messages.Children.Add(new Border
        {
            Background = ErrorBubble,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 7, 12, 8),
            Margin = new Thickness(0, 4, 30, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = "⚠ " + text, TextWrapping = TextWrapping.Wrap, Foreground = Ink },
        });
        ScrollToEnd();
    }

    static void FillInlines(InlineCollection inlines, string text, bool fromUser)
    {
        var pos = 0;
        foreach (Match m in InlineFormat.Matches(text))
        {
            if (m.Index > pos)
                inlines.Add(new Run(text[pos..m.Index]));

            if (m.Groups["bold"].Success)
            {
                inlines.Add(new Bold(new Run(m.Groups["bold"].Value)));
            }
            else
            {
                var href = m.Groups["href"].Success ? m.Groups["href"].Value : m.Groups["url"].Value;
                var label = m.Groups["label"].Success ? m.Groups["label"].Value : ShortenUrl(href);
                var link = new Hyperlink(new Run(label))
                {
                    NavigateUri = new Uri(href),
                    Foreground = fromUser ? Brushes.White : LinkBrush,
                    ToolTip = href,
                };
                link.RequestNavigate += (_, e) =>
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    e.Handled = true;
                };
                inlines.Add(link);
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            inlines.Add(new Run(text[pos..]));
    }

    static string ShortenUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "") : url;

    static ContextMenu CopyMenu(string text)
    {
        var item = new MenuItem { Header = "Kopieren" };
        item.Click += (_, _) => Clipboard.SetText(text);
        var menu = new ContextMenu();
        menu.Items.Add(item);
        return menu;
    }

    void ScrollToEnd() => Dispatcher.BeginInvoke(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);

    Storyboard CreateTypingAnimation()
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var dots = new[] { Dot1, Dot2, Dot3 };
        for (var i = 0; i < dots.Length; i++)
        {
            var anim = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i * 160) };
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));
            Storyboard.SetTarget(anim, dots[i]);
            Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
            sb.Children.Add(anim);
        }
        return sb;
    }

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ------------------------------------------------------------------
    //  UI-Ereignisse
    // ------------------------------------------------------------------

    void Send_Click(object sender, RoutedEventArgs e) => Send();

    void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var caret = Input.CaretIndex;
            Input.Text = Input.Text.Insert(caret, Environment.NewLine);
            Input.CaretIndex = caret + Environment.NewLine.Length;
        }
        else if (_cts is null)
        {
            Send();
        }
    }

    void Input_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseChat();
            e.Handled = true;
        }
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ein Klick auf die Kopfzeile lässt Meowl kurz hüpfen
        _pet.PlayAction("happy");
    }

    void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _agent.Reset();
        Messages.Children.Clear();
        AddMessage(false, "Alles klar, frisches Gespräch! 🐣 Was gibt's?");
        Input.Focus();
    }

    void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    void Close_Click(object sender, RoutedEventArgs e) => CloseChat();

    /// <summary>Nach dem Speichern der Einstellungen den Namen aktualisieren.</summary>
    public void RefreshSettings() => PetNameText.Text = _settings.PetName;
}
