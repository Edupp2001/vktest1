using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using Tesseract;

// Алиасы для разрешения конфликтов имён между System.Drawing.Imaging и Tesseract
using ImageFormat = System.Drawing.Imaging.ImageFormat;

public class OcrReader
{
    private readonly string _tessDataPath;

    public OcrReader(string tessDataPath)
    {
        _tessDataPath = tessDataPath;
    }

    /// <summary>
    /// Обрезает скриншот до верхней левой четверти и распознаёт средний FPS.
    /// </summary>
    public double ExtractAverageFps(string imagePath)
    {
        // 1. Загружаем скриншот и обрезаем верхнюю левую четверть
        using var fullImage = new Bitmap(imagePath);
        int cropW = (int)(fullImage.Width * 0.45);
        int cropH = (int)(fullImage.Height * 0.55);
        Console.WriteLine($"Полный размер: {fullImage.Width}x{fullImage.Height}, кроп: {cropW}x{cropH}");

        using var cropped = new Bitmap(cropW, cropH);
        using (var g = Graphics.FromImage(cropped))
        {
            g.DrawImage(fullImage, new Rectangle(0, 0, cropW, cropH),
                        new Rectangle(0, 0, cropW, cropH), GraphicsUnit.Pixel);
        }

        string croppedPath = Path.Combine(AppContext.BaseDirectory, "screenshot_cropped.png");
        cropped.Save(croppedPath, ImageFormat.Png);

        // 2. OCR с русским и английским
        using var engine = new TesseractEngine(_tessDataPath, "rus+eng", EngineMode.Default);
        // PSM 6 = "Assume a single uniform block of text" — хорошо для UI
        engine.DefaultPageSegMode = PageSegMode.SingleBlock;

        using var img = Pix.LoadFromFile(croppedPath);
        using var page = engine.Process(img);
        string text = page.GetText();

        Console.WriteLine("=== OCR TEXT ===");
        Console.WriteLine(text);
        Console.WriteLine("=== END OCR ===");

        // 3. Ищем первое совпадение "число FPS"
        // Tesseract может перепутать "FPS" на кириллицу, поэтому допускаем вариации
        // Ищем изолированное 2- или 3-значное число:
        // (?<![\d.]) — перед числом НЕ стоит цифра или точка (чтобы не поймать "44" в "591.44")
        // \d{2,3}     — 2-3 цифры
        // (?![\d.])   — после числа НЕ стоит цифра или точка
        var matches = Regex.Matches(text, @"(?<![\d.])(\d{2,3})(?![\d.])");

        if (matches.Count == 0)
            throw new Exception($"Не найдено изолированное 2-3 значное число в OCR: '{text}'");

        // Первое совпадение слева-сверху — это средний FPS (самая крупная цифра в UI)
        foreach (Match m in matches)
            Console.WriteLine($"Кандидат: {m.Value}");

        string value = matches[0].Groups[1].Value;
        Console.WriteLine($"Первое совпадение (Average FPS): {value}");
        return double.Parse(value);
    }
}