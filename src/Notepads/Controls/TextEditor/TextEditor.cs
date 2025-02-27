﻿
namespace Notepads.Controls.TextEditor
{
    using Notepads.Services;
    using Notepads.Utilities;
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Windows.ApplicationModel.DataTransfer;
    using Windows.Storage;
    using Windows.Storage.Provider;
    using Windows.System;
    using Windows.UI.Core;
    using Windows.UI.Text;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media;

    public class TextEditor : RichEditBox
    {
        public StorageFile EditingFile { get; set; }

        public Encoding Encoding { get; set; }

        public LineEnding LineEnding { get; set; }

        public bool Saved { get; set; }

        public event EventHandler<KeyRoutedEventArgs> OnSetSwitchingKeyDown;

        public event EventHandler<KeyRoutedEventArgs> OnSetClosingKeyDown;

        public event EventHandler<KeyRoutedEventArgs> OnSaveButtonClicked;

        public event EventHandler<KeyRoutedEventArgs> OnSaveAsButtonClicked;

        public event EventHandler<KeyRoutedEventArgs> OnFindButtonClicked;

        public event EventHandler<KeyRoutedEventArgs> OnFindAndReplaceButtonClicked;

        private string[] _documentLinesCache;

        public TextEditor()
        {
            DefaultStyleKey = nameof(TextEditor);
            Style = (Style)Application.Current.Resources[nameof(TextEditor)];
            IsSpellCheckEnabled = false;
            TextWrapping = EditorSettingsService.EditorDefaultTextWrapping;
            FontFamily = new FontFamily(EditorSettingsService.EditorFontFamily);
            FontSize = EditorSettingsService.EditorFontSize;
            SelectionHighlightColor = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush;
            SelectionHighlightColorWhenNotFocused = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush;
            SelectionFlyout = null;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            HandwritingView.BorderThickness = new Thickness(0);

            ContextFlyout = new TextEditorContextFlyout(this);
            TextChanging += TextEditor_TextChanging;
            Paste += async (sender, args) => await PastePlainTextFromWindowsClipboard(args);

            SetDefaultTabStop(FontFamily, FontSize);

            EditorSettingsService.OnFontFamilyChanged += (sender, fontFamily) =>
            {
                FontFamily = new FontFamily(fontFamily);
                SetDefaultTabStop(FontFamily, FontSize);
            };
            EditorSettingsService.OnFontSizeChanged += (sender, fontSize) =>
            {
                FontSize = fontSize;
                SetDefaultTabStop(FontFamily, FontSize);
            };
            EditorSettingsService.OnDefaultTextWrappingChanged += (sender, textWrapping) => { TextWrapping = textWrapping; };
            ThemeSettingsService.OnAccentColorChanged += (sender, color) =>
            {
                SelectionHighlightColor = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush;
                SelectionHighlightColorWhenNotFocused = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush;
            };
        }

        private void SetDefaultTabStop(FontFamily font, double fontSize)
        {
            Document.DefaultTabStop = FontUtility.GetTextWidth(font, fontSize, "text");
            TextDocument.DefaultTabStop = FontUtility.GetTextWidth(font, fontSize, "text");
        }

        private void TextEditor_TextChanging(RichEditBox sender, RichEditBoxTextChangingEventArgs args)
        {
            if (args.IsContentChanging)
            {
                _documentLinesCache = null;
            }
        }

        public async Task PastePlainTextFromWindowsClipboard(TextControlPasteEventArgs e)
        {
            if (e != null)
            {
                e.Handled = true;
            }

            if (!Document.CanPaste()) return;

            var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!dataPackageView.Contains(StandardDataFormats.Text)) return;
            try
            {
                var text = await dataPackageView.GetTextAsync();
                text = GetTextWithDefaultTabIndentation(text);
                Document.Selection.SetText(TextSetOptions.None, text);
                Document.Selection.StartPosition = Document.Selection.EndPosition;
            }
            catch (Exception)
            {
                // ignore
            }
        }

        public static string GetTextWithDefaultTabIndentation(string text)
        {
            if (EditorSettingsService.EditorDefaultTabIndents == -1) return text;
            var tabStr = new string(' ', EditorSettingsService.EditorDefaultTabIndents);
            text = text.Replace("\t", tabStr);
            return text;
        }

        public async Task<bool> SaveFile(StorageFile file)
        {
            // Prevent updates to the remote version of the file until we 
            // finish making changes and call CompleteUpdatesAsync.
            CachedFileManager.DeferUpdates(file);
            // write to file

            Document.GetText(TextGetOptions.None, out var text);

            text = TrimTextAndFixLineEnding(text, LineEnding);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Encoding encoding = Encoding ?? Encoding.UTF8;

            try
            {
                using (var stream = await file.OpenStreamForWriteAsync())
                using (var reader = new StreamReader(stream, encoding))
                using (var writer = new StreamWriter(stream, encoding))
                {
                    // Read
                    var contents = reader.ReadToEnd();

                    // Write out transformed contents from the start of the file
                    stream.Position = 0;
                    writer.Write(text);
                    writer.Flush();

                    // Truncate
                    stream.SetLength(stream.Position);
                }
            }
            catch (Exception)
            {
                return false;
            }

            // Let Windows know that we're finished changing the file so the 
            // other app can update the remote version of the file.
            FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
            if (status != FileUpdateStatus.Complete)
            {
                return false;
            }
            else
            {
                EditingFile = file;
                Encoding = encoding;
                Saved = true;

                return true;
            }
        }

        public string GetContentForSharing()
        {
            string content;

            if (Document.Selection.StartPosition == Document.Selection.EndPosition)
            {
                Document.GetText(TextGetOptions.None, out content);
            }
            else
            {
                content = Document.Selection.Text;
            }

            return content;
        }

        private static int IndexOfWholeWord(string target, int startIndex, string value, StringComparison comparison)
        {
            int pos = startIndex;
            while (pos < target.Length && (pos = target.IndexOf(value, pos, comparison)) != -1)
            {
                bool startBoundary = true;
                if (pos > 0)
                    startBoundary = !Char.IsLetterOrDigit(target[pos - 1]);

                bool endBoundary = true;
                if (pos + value.Length < target.Length)
                    endBoundary = !Char.IsLetterOrDigit(target[pos + value.Length]);

                if (startBoundary && endBoundary)
                    return pos;

                pos++;
            }
            return -1;
        }

        public bool FindNextAndReplace(string searchText, string replaceText, bool matchCase, bool matchWholeWord)
        {
            if (FindNextAndSelect(searchText, matchCase, matchWholeWord))
            {
                Document.Selection.SetText(TextSetOptions.None, replaceText);
                return true;
            }

            return false;
        }

        public bool FindAndReplaceAll(string searchText, string replaceText, bool matchCase, bool matchWholeWord)
        {
            var found = false;

            var pos = 0;
            var searchTextLength = searchText.Length;
            var replaceTextLength = replaceText.Length;

            Document.GetText(TextGetOptions.None, out var text);

            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            pos = matchWholeWord ? IndexOfWholeWord(text, pos, searchText, comparison) : text.IndexOf(searchText, pos, comparison);

            while (pos != -1)
            {
                found = true;
                text = text.Remove(pos, searchTextLength).Insert(pos, replaceText);
                pos += replaceTextLength;
                pos = matchWholeWord ? IndexOfWholeWord(text, pos, searchText, comparison) : text.IndexOf(searchText, pos, comparison);
            }

            if (found)
            {
                // Trim end \r
                if (!string.IsNullOrEmpty(text) && text[text.Length - 1] == '\r')
                {
                    text = text.Substring(0, text.Length - 1);
                }

                SetText(text);
                Document.Selection.StartPosition = Int32.MaxValue;
                Document.Selection.EndPosition = Document.Selection.StartPosition;
            }

            return found;
        }

        public bool FindNextAndSelect(string searchText, bool matchCase, bool matchWholeWord, bool stopAtEof = true)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                return false;
            }

            Document.GetText(TextGetOptions.None, out var text);

            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            var index = matchWholeWord ? IndexOfWholeWord(text, Document.Selection.EndPosition, searchText, comparison) : text.IndexOf(searchText, Document.Selection.EndPosition, comparison);

            if (index != -1)
            {
                Document.Selection.StartPosition = index;
                Document.Selection.EndPosition = index + searchText.Length;
            }
            else
            {
                if (!stopAtEof)
                {
                    index = matchWholeWord ? IndexOfWholeWord(text, 0, searchText, comparison) : text.IndexOf(searchText, 0, comparison);

                    if (index != -1)
                    {
                        Document.Selection.StartPosition = index;
                        Document.Selection.EndPosition = index + searchText.Length;
                    }
                }
            }

            if (index == -1)
            {
                Document.Selection.StartPosition = Document.Selection.EndPosition;
                return false;
            }

            return true;
        }

        //TODO This method I wrote is pathetic, need to find a way to implement it in a better way 
        public void GetCurrentLineColumn(out int lineIndex, out int columnIndex, out int selectedCount)
        {
            if (_documentLinesCache == null)
            {
                Document.GetText(TextGetOptions.None, out var text);
                _documentLinesCache = text.Split("\r");
            }

            var start = Document.Selection.StartPosition;
            var end = Document.Selection.EndPosition;

            lineIndex = 1;
            columnIndex = 1;
            selectedCount = 0;

            var length = 0;
            bool startLocated = false;
            for (int i = 0; i < _documentLinesCache.Length; i++)
            {
                var line = _documentLinesCache[i];

                if (line.Length + length >= start && !startLocated)
                {
                    lineIndex = i + 1;
                    columnIndex = start - length + 1;
                    startLocated = true;
                }

                if (line.Length + length >= end)
                {
                    if (i == lineIndex - 1)
                        selectedCount = end - start;
                    else
                        selectedCount = end - start + (i - lineIndex) - 1;
                    return;
                }

                length += line.Length + 1;
            }
        }

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
            var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift);

            if (ctrl.HasFlag(CoreVirtualKeyStates.Down))
            {
                // Disable RichEditBox default shortcuts (Bold, Underline, Italic)
                // https://docs.microsoft.com/en-us/windows/desktop/controls/about-rich-edit-controls
                if (e.Key == VirtualKey.B || e.Key == VirtualKey.I || e.Key == VirtualKey.U ||
                    e.Key == VirtualKey.Number1 || e.Key == VirtualKey.Number2 ||
                    e.Key == VirtualKey.Number3 || e.Key == VirtualKey.Number4 ||
                    e.Key == VirtualKey.Number5 || e.Key == VirtualKey.Number6 || e.Key == VirtualKey.Number7 ||
                    e.Key == VirtualKey.Number8 || e.Key == VirtualKey.Number9 ||
                    e.Key == VirtualKey.L || e.Key == VirtualKey.R)
                {
                    return;
                }

                if (e.Key == VirtualKey.Tab)
                {
                    OnSetSwitchingKeyDown?.Invoke(this, e);
                    return;
                }

                if (e.Key == VirtualKey.S)
                {
                    if (shift.HasFlag(CoreVirtualKeyStates.Down))
                    {
                        OnSaveAsButtonClicked?.Invoke(this, e);
                    }
                    else
                    {
                        OnSaveButtonClicked?.Invoke(this, e);
                    }
                    return;
                }

                if (e.Key == VirtualKey.W)
                {
                    if (!shift.HasFlag(CoreVirtualKeyStates.Down))
                    {
                        OnSetClosingKeyDown?.Invoke(this, e);
                    }

                    return;
                }

                if (e.Key == VirtualKey.F)
                {
                    if (shift.HasFlag(CoreVirtualKeyStates.Down))
                    {
                        OnFindAndReplaceButtonClicked?.Invoke(this, e);
                    }
                    else
                    {
                        OnFindButtonClicked?.Invoke(this, e);
                    }
                    return;
                }

                if (e.Key == VirtualKey.Z)
                {
                    if (shift.HasFlag(CoreVirtualKeyStates.Down))
                    {
                        Document.Redo();
                    }
                    else
                    {
                        Document.Undo();
                    }
                    return;
                }

                if (e.Key == (VirtualKey)187) // +
                {
                    SetDefaultTabStop(FontFamily, FontSize + 2);
                    FontSize += 2;
                    return;
                }

                if (e.Key == (VirtualKey)189) // -
                {
                    if (FontSize > 4)
                    {
                        SetDefaultTabStop(FontFamily, FontSize - 2);
                        FontSize -= 2;
                    }
                    return;
                }

                if (e.Key == VirtualKey.Number0) // 0
                {
                    SetDefaultTabStop(FontFamily, EditorSettingsService.EditorFontSize);
                    FontSize = EditorSettingsService.EditorFontSize;
                    return;
                }
            }
            base.OnKeyDown(e);
        }

        private string TrimTextAndFixLineEnding(string text, LineEnding lineEnding)
        {
            // Trim end \r
            if (!string.IsNullOrEmpty(text) && text[text.Length - 1] == '\r')
            {
                text = text.Substring(0, text.Length - 1);
            }

            return LineEndingUtility.ApplyLineEnding(text, lineEnding);
        }

        public void SetText(string text)
        {
            Document.SetText(TextSetOptions.None, GetTextWithDefaultTabIndentation(text));
        }
    }
}
