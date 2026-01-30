using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SipAiGateway;

public sealed class TextBoxWriter : TextWriter
{
    private readonly TextBox _textBox;
    private readonly TextWriter? _fileWriter;
    private readonly object _lock = new();
    [ThreadStatic]
    private static bool _isWriting;

    public TextBoxWriter(TextBox textBox, TextWriter? fileWriter = null)
    {
        _textBox = textBox;
        _fileWriter = fileWriter;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (_isWriting)
        {
            return;
        }

        _isWriting = true;
        try
        {
            var text = value.ToString();
            AppendText(text);
            WriteToFile(text);
        }
        catch
        {
            // Swallow logging exceptions to avoid recursive failures.
        }
        finally
        {
            _isWriting = false;
        }
    }

    public override void Write(string? value)
    {
        if (_isWriting)
        {
            return;
        }

        _isWriting = true;
        try
        {
            if (value is null) return;
            AppendText(value);
            WriteToFile(value);
        }
        catch
        {
            // Swallow logging exceptions to avoid recursive failures.
        }
        finally
        {
            _isWriting = false;
        }
    }

    public override void WriteLine(string? value)
    {
        if (_isWriting)
        {
            return;
        }

        _isWriting = true;
        try
        {
            var text = (value ?? string.Empty) + Environment.NewLine;
            AppendText(text);
            WriteToFile(text);
        }
        catch
        {
            // Swallow logging exceptions to avoid recursive failures.
        }
        finally
        {
            _isWriting = false;
        }
    }

    private void AppendText(string text)
    {
        try
        {
            if (_textBox.IsDisposed) return;

            if (_textBox.InvokeRequired)
            {
                _textBox.BeginInvoke(new Action<string>(AppendText), text);
                return;
            }

            _textBox.AppendText(text);
        }
        catch
        {
            // Swallow UI logging exceptions to avoid recursive failures.
        }
    }

    private void WriteToFile(string text)
    {
        if (_fileWriter == null)
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                _fileWriter.Write(text);
                _fileWriter.Flush();
            }
        }
        catch
        {
            // Swallow file logging exceptions to avoid recursive failures.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                _fileWriter?.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
