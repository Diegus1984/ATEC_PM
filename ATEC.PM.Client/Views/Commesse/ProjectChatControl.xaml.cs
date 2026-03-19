using System.Windows.Media;
using System.Windows.Threading;

namespace ATEC.PM.Client.UserControls;

public partial class ProjectChatControl : UserControl
{
    private int _projectId;
    private int _selectedChatId;
    private DispatcherTimer? _pollTimer;
    private List<ChatParticipantDto> _currentParticipants = new();
    private Popup? _mentionPopup;
    private ListBox? _mentionList;


    private static SolidColorBrush B(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public ProjectChatControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => _pollTimer?.Stop();
    }

    public async void Load(int projectId)
    {
        _projectId = projectId;
        await LoadChatList();
        StartPolling();
    }

    private void StartPolling()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _pollTimer.Tick += async (_, _) =>
        {
            await LoadChatList();
            if (_selectedChatId > 0) await LoadMessages(_selectedChatId, scroll: false);
        };
        _pollTimer.Start();
    }

    // ═══ CHAT LIST ═══

    private async Task LoadChatList()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/chat/project/{_projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var chats = JsonSerializer.Deserialize<List<ChatListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            RenderChatList(chats);
        }
        catch { }
    }

    private void RenderChatList(List<ChatListItem> chats)
    {
        pnlChatList.Children.Clear();

        if (!chats.Any())
        {
            pnlChatList.Children.Add(new TextBlock
            {
                Text = "Nessuna chat.\nClicca + per crearne una.",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(16, 20, 16, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (ChatListItem chat in chats)
        {
            Border item = new()
            {
                Padding = new Thickness(12, 8, 12, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = chat.Id == _selectedChatId ? B("#EEF2FF") : Brushes.White,
                BorderBrush = B("#E4E7EC"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            StackPanel sp = new();
            sp.Children.Add(new TextBlock
            {
                Text = chat.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = B("#1A1D26")
            });

            DockPanel infoRow = new() { Margin = new Thickness(0, 2, 0, 0) };
            infoRow.Children.Add(new TextBlock
            {
                Text = $"👥 {chat.ParticipantCount}  💬 {chat.MessageCount}",
                FontSize = 10,
                Foreground = B("#6B7280")
            });
            if (chat.LastMessageAt.HasValue)
            {
                TextBlock timeText = new()
                {
                    Text = chat.LastMessageAt.Value.ToString("dd/MM HH:mm"),
                    FontSize = 10,
                    Foreground = B("#9CA3AF"),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                DockPanel.SetDock(timeText, Dock.Right);
                infoRow.Children.Add(timeText);
            }
            sp.Children.Add(infoRow);

            if (!string.IsNullOrEmpty(chat.LastMessagePreview))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = chat.LastMessagePreview,
                    FontSize = 11,
                    Foreground = B("#9CA3AF"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            // Badge non letti
            if (chat.UnreadCount > 0)
            {
                Border unreadBadge = new()
                {
                    Background = B("#F59E0B"),
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -2, 4, 0)
                };
                unreadBadge.Child = new TextBlock
                {
                    Text = chat.UnreadCount > 99 ? "99+" : chat.UnreadCount.ToString(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                sp.Children.Add(unreadBadge);
            }

            item.Child = sp;

            int chatId = chat.Id;
            string chatTitle = chat.Title;
            int partCount = chat.ParticipantCount;

            item.MouseLeftButtonUp += async (s, e) =>
             {
                 _selectedChatId = chatId;
                 txtChatTitle.Text = chatTitle;
                 txtParticipants.Text = $"👥 {partCount} partecipanti";
                 txtMessage.IsEnabled = true;
                 btnSend.IsEnabled = true;
                 btnAttach.IsEnabled = true;
                 btnDeleteChat.Visibility = Visibility.Visible;
                 await LoadChatList();
                 await LoadMessages(chatId);
                 await LoadCurrentParticipants(chatId);
                 await ApiClient.PostAsync($"/api/chat/{chatId}/mark-read", "{}");
             };

            pnlChatList.Children.Add(item);
        }
    }

    // ═══ MESSAGES ═══

    private async Task LoadMessages(int chatId, bool scroll = true)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/chat/{chatId}/messages");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var messages = JsonSerializer.Deserialize<List<ChatMessageDto>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            RenderMessages(messages);
            if (scroll) scrollMessages.ScrollToEnd();
        }
        catch { }
    }

    private async Task LoadCurrentParticipants(int chatId)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/chat/{chatId}/participants");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                _currentParticipants = JsonSerializer.Deserialize<List<ChatParticipantDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Where(p => p.EmployeeId != App.UserId).ToList() ?? new();
        }
        catch { _currentParticipants = new(); }
    }

    private void RenderMessages(List<ChatMessageDto> messages)
    {
        pnlMessages.Children.Clear();

        if (!messages.Any())
        {
            pnlMessages.Children.Add(new TextBlock
            {
                Text = "Nessun messaggio. Scrivi il primo!",
                FontSize = 12,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
            return;
        }

        string lastDate = "";
        foreach (ChatMessageDto msg in messages)
        {
            // Separatore data
            string dateStr = msg.CreatedAt.ToString("dd/MM/yyyy");
            if (dateStr != lastDate)
            {
                Border dateSep = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(12, 3, 12, 3),
                    Margin = new Thickness(0, 12, 0, 8),
                    Background = B("#E4E7EC")
                };
                dateSep.Child = new TextBlock { Text = dateStr, FontSize = 10, Foreground = B("#6B7280") };
                pnlMessages.Children.Add(dateSep);
                lastDate = dateStr;
            }

            // Bolla messaggio
            HorizontalAlignment align = msg.IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            string bubbleColor = msg.IsMine ? "#4F6EF7" : "#FFFFFF";
            string textColor = msg.IsMine ? "#FFFFFF" : "#1A1D26";

            StackPanel bubble = new()
            {
                HorizontalAlignment = align,
                MaxWidth = 450,
                Margin = new Thickness(0, 2, 0, 2)
            };

            // Nome (solo per messaggi altrui)
            if (!msg.IsMine)
            {
                DockPanel nameRow = new() { Margin = new Thickness(0, 0, 0, 2) };
                Border avatar = new()
                {
                    Width = 22,
                    Height = 22,
                    Background = B("#4F6EF71A"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                avatar.Child = new TextBlock
                {
                    Text = msg.EmployeeInitials,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = B("#4F6EF7"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                nameRow.Children.Add(avatar);
                nameRow.Children.Add(new TextBlock
                {
                    Text = msg.EmployeeName,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = B("#6B7280"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                bubble.Children.Add(nameRow);
            }

            Border msgBorder = new()
            {
                Background = B(bubbleColor),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = msg.IsMine ? B(bubbleColor) : B("#E4E7EC"),
                BorderThickness = new Thickness(1)
            };

            StackPanel msgContent = new();
            msgContent.Children.Add(BuildMessageText(msg.Message, textColor, msg.IsMine));

            // Allegato: immagine inline o link file
            if (msg.HasAttachment && !string.IsNullOrEmpty(msg.AttachmentName))
            {
                string ext = System.IO.Path.GetExtension(msg.AttachmentName).ToLower();
                string attachPath = msg.AttachmentPath;

                if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif")
                {
                    try
                    {
                        if (System.IO.File.Exists(attachPath))
                        {
                            var bmp = new System.Windows.Media.Imaging.BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(attachPath);
                            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmp.DecodePixelWidth = 300;
                            bmp.EndInit();

                            Image img = new()
                            {
                                Source = bmp,
                                MaxWidth = 300,
                                MaxHeight = 250,
                                Stretch = Stretch.Uniform,
                                Margin = new Thickness(0, 6, 0, 0),
                                Cursor = System.Windows.Input.Cursors.Hand
                            };
                            img.MouseLeftButtonUp += (s, ev) =>
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(attachPath) { UseShellExecute = true });
                            };
                            msgContent.Children.Add(img);
                        }
                    }
                    catch { }
                }
                else
                {
                    TextBlock linkText = new()
                    {
                        Text = $"📎 {msg.AttachmentName}",
                        FontSize = 12,
                        Foreground = msg.IsMine ? Brushes.White : B("#4F6EF7"),
                        TextDecorations = TextDecorations.Underline,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    linkText.MouseLeftButtonUp += (s, ev) =>
                    {
                        try
                        {
                            if (System.IO.File.Exists(attachPath))
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(attachPath) { UseShellExecute = true });
                            else
                                MessageBox.Show("File non trovato.");
                        }
                        catch { }
                    };
                    msgContent.Children.Add(linkText);
                }
            }

            msgContent.Children.Add(new TextBlock
            {
                Text = msg.CreatedAt.ToString("HH:mm"),
                FontSize = 9,
                Foreground = msg.IsMine ? B("#FFFFFF80") : B("#9CA3AF"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            });

            msgBorder.Child = msgContent;

            // Context menu per eliminazione
            if (msg.IsMine || App.CurrentUser.IsAdmin)
            {
                ContextMenu ctx = new();
                MenuItem delItem = new() { Header = "Elimina messaggio" };
                int msgId = msg.Id;
                delItem.Click += async (s, ev) =>
                {
                    try
                    {
                        string result = await ApiClient.DeleteAsync($"/api/chat/messages/{msgId}");
                        JsonDocument doc = JsonDocument.Parse(result);
                        if (doc.RootElement.GetProperty("success").GetBoolean())
                            await LoadMessages(_selectedChatId);
                    }
                    catch { }
                };
                ctx.Items.Add(delItem);
                msgBorder.ContextMenu = ctx;
            }

            bubble.Children.Add(msgBorder);
            pnlMessages.Children.Add(bubble);
        }
    }

    // ═══ SEND MESSAGE ═══

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        await SendMessage();
    }

    private void TxtMessage_KeyDown(object sender, KeyEventArgs e)
    {
        if (_mentionPopup != null && _mentionPopup.IsOpen)
        {
            if (e.Key == Key.Down && _mentionList != null)
            {
                _mentionList.SelectedIndex = Math.Min(_mentionList.SelectedIndex + 1, _mentionList.Items.Count - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && _mentionList != null)
            {
                _mentionList.SelectedIndex = Math.Max(_mentionList.SelectedIndex - 1, 0);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _mentionList?.SelectedItem != null)
            {
                InsertMention((ChatParticipantDto)_mentionList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseMentionPopup();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = SendMessage();
        }
    }

    private void TxtMessage_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_selectedChatId == 0 || !_currentParticipants.Any()) return;

        string text = txtMessage.Text;
        int caretPos = txtMessage.CaretIndex;

        // Cerca l'ultima @ prima del cursore
        int atIndex = text.LastIndexOf('@', Math.Max(0, caretPos - 1));
        if (atIndex < 0 || atIndex >= caretPos)
        {
            CloseMentionPopup();
            return;
        }

        // Prendi il testo dopo @
        string filter = text.Substring(atIndex + 1, caretPos - atIndex - 1).ToLower();

        // Se c'è uno spazio dopo @ e poi altro testo con spazio, probabilmente ha già finito
        if (filter.Contains(' ') && filter.Split(' ').Length > 2)
        {
            CloseMentionPopup();
            return;
        }

        var filtered = _currentParticipants
            .Where(p => p.EmployeeName.ToLower().Contains(filter))
            .ToList();

        if (filtered.Any())
            ShowMentionPopup(filtered);
        else
            CloseMentionPopup();
    }

    private void ShowMentionPopup(List<ChatParticipantDto> participants)
    {
        if (_mentionPopup == null)
        {
            _mentionList = new ListBox
            {
                MaxHeight = 150,
                Width = 250,
                FontSize = 13,
                BorderBrush = B("#E4E7EC"),
                BorderThickness = new Thickness(1),
                DisplayMemberPath = "EmployeeName"
            };
            _mentionList.MouseLeftButtonUp += (s, e) =>
            {
                if (_mentionList.SelectedItem is ChatParticipantDto p)
                    InsertMention(p);
            };

            _mentionPopup = new Popup
            {
                PlacementTarget = txtMessage,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                StaysOpen = false,
                Child = _mentionList
            };
        }

        _mentionList!.ItemsSource = participants;
        if (participants.Any()) _mentionList.SelectedIndex = 0;
        _mentionPopup!.IsOpen = true;
    }

    private void CloseMentionPopup()
    {
        if (_mentionPopup != null)
            _mentionPopup.IsOpen = false;
    }

    private void InsertMention(ChatParticipantDto participant)
    {
        string text = txtMessage.Text;
        int caretPos = txtMessage.CaretIndex;
        int atIndex = text.LastIndexOf('@', Math.Max(0, caretPos - 1));

        if (atIndex >= 0)
        {
            string before = text.Substring(0, atIndex);
            string after = caretPos < text.Length ? text.Substring(caretPos) : "";
            string mention = $"@{participant.EmployeeName} ";

            txtMessage.Text = before + mention + after;
            txtMessage.CaretIndex = before.Length + mention.Length;
        }

        CloseMentionPopup();
        txtMessage.Focus();
    }

    private async Task SendMessage()
    {
        if (_selectedChatId == 0 || string.IsNullOrWhiteSpace(txtMessage.Text)) return;

        string message = txtMessage.Text.Trim();
        txtMessage.Text = "";

        try
        {
            string jsonBody = JsonSerializer.Serialize(new { chatId = _selectedChatId, message },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/chat/{_selectedChatId}/messages", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await LoadMessages(_selectedChatId);
                await LoadChatList();
            }
        }
        catch { }
    }

    // ═══ NEW CHAT ═══

    private async void BtnNewChat_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewChatDialog(_projectId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            _selectedChatId = dlg.CreatedChatId;
            await LoadChatList();
            await LoadMessages(_selectedChatId);
            txtChatTitle.Text = dlg.ChatTitle;
            txtMessage.IsEnabled = true;
            btnSend.IsEnabled = true;
            btnDeleteChat.Visibility = Visibility.Visible;
        }
    }
    private static TextBlock BuildMessageText(string message, string textColor, bool isMine)
    {
        TextBlock tb = new()
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };

        // Parsa @menzioni
        int i = 0;
        while (i < message.Length)
        {
            int atIdx = message.IndexOf('@', i);
            if (atIdx < 0)
            {
                tb.Inlines.Add(new System.Windows.Documents.Run(message.Substring(i))
                { Foreground = B(textColor) });
                break;
            }

            // Testo prima della @
            if (atIdx > i)
                tb.Inlines.Add(new System.Windows.Documents.Run(message.Substring(i, atIdx - i))
                { Foreground = B(textColor) });

            // Trova la fine del nome (cerca due parole dopo @)
            int nameStart = atIdx + 1;
            int spaceCount = 0;
            int nameEnd = nameStart;
            while (nameEnd < message.Length)
            {
                if (message[nameEnd] == ' ') spaceCount++;
                if (spaceCount >= 2 || (spaceCount >= 1 && nameEnd > nameStart + 1 && !char.IsLetter(message[nameEnd]) && message[nameEnd] != ' '))
                    break;
                nameEnd++;
            }

            string mention = message.Substring(atIdx, nameEnd - atIdx);
            string mentionColor = isMine ? "#FFFFFF" : "#4F6EF7";
            tb.Inlines.Add(new System.Windows.Documents.Run(mention)
            {
                Foreground = B(mentionColor),
                FontWeight = FontWeights.Bold
            });

            i = nameEnd;
        }

        return tb;
    }

    // ═══ DELETE CHAT ═══

    private async void BtnDeleteChat_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedChatId == 0) return;

        if (MessageBox.Show("Eliminare questa chat e tutti i messaggi?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/chat/{_selectedChatId}");
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _selectedChatId = 0;
                txtChatTitle.Text = "Seleziona una chat";
                txtParticipants.Text = "";
                txtMessage.IsEnabled = false;
                btnSend.IsEnabled = false;
                btnAttach.IsEnabled = false;
                btnDeleteChat.Visibility = Visibility.Collapsed;
                pnlMessages.Children.Clear();
                await LoadChatList();
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnAttach_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedChatId == 0) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleziona file da allegare",
            Filter = "Tutti i file (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        string filePath = dlg.FileName;
        string fileName = System.IO.Path.GetFileName(filePath);

        try
        {
            // Upload file
            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
            string base64 = Convert.ToBase64String(fileBytes);

            string jsonBody = JsonSerializer.Serialize(new
            {
                chatId = _selectedChatId,
                message = $"📎 {fileName}",
                fileName,
                fileData = base64
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/chat/{_selectedChatId}/messages/with-attachment", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await LoadMessages(_selectedChatId);
                await LoadChatList();
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore upload: {ex.Message}"); }
    }
}
