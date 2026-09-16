using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace GtaCasinoAssistant
{
    public static class OfflineTestProgram
    {
        private static readonly int[][] ExpectedSlots = new int[][]
        {
            new int[] { 1, 2, 4, 5 },
            new int[] { 3, 5, 6, 7 },
            new int[] { 1, 2, 5, 7 },
            new int[] { 2, 4, 5, 6 },
            new int[] { 1, 3, 5, 7 }
        };

        private static readonly int[] ExpectedTargets = new int[] { 1, 2, 3, 4, 4 };

        public static void Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--license")
            {
                bool valid = LicenseClient.ValidateTokenForTest(args[1], args[2]);
                Console.WriteLine("license signature: " + (valid ? "PASS" : "FAIL"));
                Environment.ExitCode = valid ? 0 : 1;
                return;
            }
            if (args.Length == 1 && args[0] == "--bridge")
            {
                RunSimulatorBridge();
                return;
            }
            if (args.Length == 1 && args[0] == "--new-hacks")
            {
                RunNewHackTests();
                return;
            }
            if (args.Length >= 2 && args[0] == "--cayo")
            {
                RunCayo(args, 1);
                return;
            }
            if (args.Length == 2 && args[0] == "--cayo-multires")
            {
                RunCayoMultiResolution(args[1]);
                return;
            }
            if (args.Length == 2 && args[0] == "--voltage")
            {
                using (Bitmap screenshot = new Bitmap(args[1]))
                {
                    CayoVoltageResult result;
                    bool read = CayoVoltageScanner.ScanBitmap(screenshot, out result);
                    Console.WriteLine(read ? "PASS target=" + result.Target + " route=" + string.Join(",", result.Assignment) : "FAIL");
                    Environment.ExitCode = read ? 0 : 1;
                }
                return;
            }
            if (args.Length == 2 && args[0] == "--keypad")
            {
                using (Bitmap screenshot = new Bitmap(args[1]))
                {
                    CasinoKeypadResult result;
                    bool read = CasinoKeypadScanner.ScanPattern(screenshot, out result);
                    Console.WriteLine(read ? "PASS rows=" + string.Join(",", result.Rows) : "FAIL");
                    Environment.ExitCode = read ? 0 : 1;
                }
                return;
            }
            if (args.Length == 3 && args[0] == "--keypad-ring")
            {
                using (Bitmap screenshot = new Bitmap(args[1]))
                {
                    int row;
                    bool read = CasinoKeypadScanner.TryDetectRingRow(screenshot, Int32.Parse(args[2]), out row);
                    Console.WriteLine(read ? "PASS row=" + row : "FAIL");
                    Environment.ExitCode = read ? 0 : 1;
                }
                return;
            }
            if (args.Length == 2 && args[0] == "--keypad-stage")
            {
                using (Bitmap screenshot = new Bitmap(args[1]))
                {
                    bool inputStage = CasinoKeypadScanner.IsInputStage(screenshot);
                    Console.WriteLine(inputStage ? "INPUT" : "PATTERN");
                    Environment.ExitCode = inputStage ? 1 : 0;
                }
                return;
            }
            if (args.Length == 2 && args[0] == "--probe")
            {
                RunProbe(args[1]);
                return;
            }
            if (args.Length == 5 && args[0] == "--casino-expect")
            {
                RunCasinoExpectation(args[1], Int32.Parse(args[2]), args[3], Int32.Parse(args[4]));
                return;
            }

            if (args.Length < 4 || args.Length > 5)
            {
                Console.Error.WriteLine("Usage: GtaCasinoAssistant.Tests.exe <fingerprint1.png> <fingerprint2.png> <fingerprint3.png> <fingerprint4.png> [fingerprint4-16x9.png]");
                Environment.ExitCode = 2;
                return;
            }

            FingerprintDatabase database = new FingerprintDatabase();
            database.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates"));
            bool allPassed = true;
            for (int index = 0; index < args.Length; index++)
            {
                using (Bitmap screenshot = new Bitmap(args[index]))
                {
                    FingerprintTarget target;
                    List<int> slots;
                    double confidence;
                    bool detected = ReliableAutoScanner.ScanBitmap(database, screenshot, out target, out slots, out confidence);
                    bool passed = detected && target != null && target.Id == ExpectedTargets[index] && Same(slots, ExpectedSlots[index]);
                    Console.WriteLine(
                        "#{0}: {1} target={2} slots=[{3}] score={4:F3}",
                        index + 1,
                        passed ? "PASS" : "FAIL",
                        target == null ? 0 : target.Id,
                        string.Join(",", slots.ConvertAll(value => value.ToString()).ToArray()),
                        confidence);
                    allPassed &= passed;
                }
            }

            Environment.ExitCode = allPassed ? 0 : 1;
        }

        private static void RunSimulatorBridge()
        {
            string statePath = Path.Combine(Path.GetTempPath(), "lsf-cayo-simulator.state");
            try
            {
                File.WriteAllText(statePath, "offline|4|0,3,2,7,4,5,6,7");
                CayoFingerprintResult result;
                bool ok = CayoSimulatorBridge.TryRead(out result);
                bool passed = ok && result.CursorRow == 4 && SameClicks(result.Clicks, new int[] { 0, -2, 0, 4, 0, 0, 0, 0 });
                Console.WriteLine("simulator bridge: " + (passed ? "PASS" : "FAIL"));
                Environment.ExitCode = passed ? 0 : 1;
            }
            finally { try { File.Delete(statePath); } catch { } }
        }

        private static bool SameClicks(int[] actual, int[] expected)
        {
            if (actual == null || actual.Length != expected.Length) return false;
            for (int index = 0; index < actual.Length; index++) if (actual[index] != expected[index]) return false;
            return true;
        }

        private static void RunCayo(string[] args, int firstImageIndex)
        {
            bool allRead = true;
            for (int index = firstImageIndex; index < args.Length; index++)
            {
                using (Bitmap screenshot = new Bitmap(args[index]))
                {
                    CayoFingerprintResult result;
                    bool read = CayoFingerprintScanner.ScanBitmap(screenshot, out result);
                    Console.WriteLine("{0}: {1} cursor={2} clicks=[{3}] score={4:F3} target={5}",
                        Path.GetFileName(args[index]), read ? "PASS" : "FAIL",
                        result == null ? -1 : result.CursorRow,
                        result == null ? "" : string.Join(",", result.Clicks),
                        result == null ? 0 : result.Confidence,
                        result == null ? "" : result.TargetSignature);
                    Console.WriteLine("  diagnostic: " + CayoFingerprintScanner.LastDiagnostic);
                    allRead &= read;
                }
            }
            Environment.ExitCode = allRead ? 0 : 1;
        }

        private static void RunCayoMultiResolution(string imagePath)
        {
            bool allPassed = true;
            using (Bitmap source = new Bitmap(imagePath))
            {
                CayoFingerprintResult expected;
                if (!CayoFingerprintScanner.ScanBitmap(source, out expected))
                {
                    Console.WriteLine("source: FAIL " + CayoFingerprintScanner.LastDiagnostic);
                    Environment.ExitCode = 1;
                    return;
                }
                string[,] cases = MultiResolutionCases();
                for (int index = 0; index < cases.GetLength(0); index++)
                {
                    int width = Int32.Parse(cases[index, 0]);
                    int height = Int32.Parse(cases[index, 1]);
                    string layout = cases[index, 2];
                    using (Bitmap frame = RenderLayout(source, width, height, layout))
                    {
                        CayoFingerprintResult actual;
                        bool passed = CayoFingerprintScanner.ScanBitmap(frame, out actual)
                            && actual.CursorRow == expected.CursorRow
                            && SameArray(actual.Clicks, expected.Clicks);
                        allPassed &= passed;
                        Console.WriteLine("cayo {0}x{1} {2}: {3} {4}", width, height, layout,
                            passed ? "PASS" : "FAIL", CayoFingerprintScanner.LastDiagnostic);
                        if (!passed)
                        {
                            Console.WriteLine("  expected cursor={0} clicks=[{1}]", expected.CursorRow, string.Join(",", expected.Clicks));
                            Console.WriteLine("  actual cursor={0} clicks=[{1}]",
                                actual == null ? -1 : actual.CursorRow,
                                actual == null ? "" : string.Join(",", actual.Clicks));
                        }
                    }
                }
            }
            Environment.ExitCode = allPassed ? 0 : 1;
        }

        private static void RunProbe(string imagePath)
        {
            FingerprintDatabase database = new FingerprintDatabase();
            database.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates"));
            using (Bitmap screenshot = new Bitmap(imagePath))
            {
                CayoFingerprintResult cayo;
                bool cayoDetected = CayoFingerprintScanner.ScanBitmap(screenshot, out cayo);
                FingerprintTarget casinoTarget;
                List<int> casinoSlots;
                double casinoConfidence;
                bool casinoDetected = ReliableAutoScanner.ScanBitmap(database, screenshot, out casinoTarget, out casinoSlots, out casinoConfidence);
                Console.WriteLine("cayo={0} score={1:F3}; casino={2} target={3} slots=[{4}] score={5:F3}",
                    cayoDetected, cayo == null ? 0 : cayo.Confidence,
                    casinoDetected, casinoTarget == null ? 0 : casinoTarget.Id,
                    casinoSlots == null ? "" : string.Join(",", casinoSlots.ConvertAll(value => value.ToString()).ToArray()),
                    casinoConfidence);
            }
        }

        private static void RunCasinoExpectation(string imagePath, int expectedTarget, string expectedSlotText, int expectedCursor)
        {
            string[] parts = expectedSlotText.Split(',');
            int[] expectedSlots = Array.ConvertAll(parts, value => Int32.Parse(value));
            FingerprintDatabase database = new FingerprintDatabase();
            database.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates"));
            using (Bitmap screenshot = new Bitmap(imagePath))
            {
                FingerprintTarget target;
                List<int> slots;
                double confidence;
                bool detected = ReliableAutoScanner.ScanBitmap(database, screenshot, out target, out slots, out confidence);
                int cursor = ReliableAutoScanner.DetectCursorSlot(screenshot);
                bool passed = detected && target != null && target.Id == expectedTarget
                    && Same(slots, expectedSlots) && cursor == expectedCursor;
                Console.WriteLine("casino fixture: {0} target={1} slots=[{2}] cursor={3} score={4:F3}",
                    passed ? "PASS" : "FAIL", target == null ? 0 : target.Id,
                    slots == null ? "" : string.Join(",", slots.ConvertAll(value => value.ToString()).ToArray()),
                    cursor, confidence);
                Environment.ExitCode = passed ? 0 : 1;
            }
        }

        private static void RunNewHackTests()
        {
            int[] keypadExpected = new int[] { 1, 3, 5, 2, 4, 1 };
            bool allPassed = true;
            using (Bitmap voltage = BuildVoltageFixture())
            using (Bitmap keypad = BuildKeypadFixture(keypadExpected, 3, 4))
            {
                string[,] cases = MultiResolutionCases();
                for (int index = 0; index < cases.GetLength(0); index++)
                {
                    int width = Int32.Parse(cases[index, 0]);
                    int height = Int32.Parse(cases[index, 1]);
                    string layout = cases[index, 2];
                    using (Bitmap voltageFrame = RenderLayout(voltage, width, height, layout))
                    using (Bitmap keypadFrame = RenderLayout(keypad, width, height, layout))
                    {
                        CayoVoltageResult voltageResult;
                        bool voltagePassed = CayoVoltageScanner.ScanBitmap(voltageFrame, out voltageResult)
                            && voltageResult.Target == 100
                            && SameArray(voltageResult.LeftNumbers, new int[] { 8, 7, 6 })
                            && SameArray(voltageResult.RightMultipliers, new int[] { 10, 2, 1 })
                            && SameArray(voltageResult.Assignment, new int[] { 0, 1, 2 });

                        CasinoKeypadResult keypadResult;
                        bool keypadPassed = CasinoKeypadScanner.ScanPattern(keypadFrame, out keypadResult)
                            && SameArray(keypadResult.Rows, keypadExpected);
                        int ringRow;
                        bool ringPassed = CasinoKeypadScanner.TryDetectRingRow(keypadFrame, 3, out ringRow) && ringRow == 4;

                        bool passed = voltagePassed && keypadPassed && ringPassed;
                        allPassed &= passed;
                        Console.WriteLine("{0}x{1} {2}: {3} voltage={4} keypad={5} ring={6}",
                            width, height, layout, passed ? "PASS" : "FAIL", voltagePassed, keypadPassed, ringPassed);
                        if (!passed)
                        {
                            Console.WriteLine("  voltage: " + CayoVoltageScanner.LastDiagnostic);
                            Console.WriteLine("  keypad: " + CasinoKeypadScanner.LastPatternDiagnostic);
                            Console.WriteLine("  ring: " + CasinoKeypadScanner.LastRingDiagnostic);
                        }
                    }
                }

                using (Bitmap dimVoltage = AdjustVisual(voltage, 0.32f, 0))
                using (Bitmap dimKeypad = AdjustVisual(keypad, 0.42f, 0))
                using (Bitmap dimVoltageFrame = RenderLayout(dimVoltage, 1600, 900, "stretch"))
                using (Bitmap dimKeypadFrame = RenderLayout(dimKeypad, 1600, 900, "stretch"))
                {
                    CayoVoltageResult voltageResult;
                    CasinoKeypadResult keypadResult;
                    bool voltagePassed = CayoVoltageScanner.ScanBitmap(dimVoltageFrame, out voltageResult);
                    bool keypadPassed = CasinoKeypadScanner.ScanPattern(dimKeypadFrame, out keypadResult)
                        && SameArray(keypadResult.Rows, keypadExpected);
                    bool passed = voltagePassed && keypadPassed;
                    allPassed &= passed;
                    Console.WriteLine("dim-quality 1600x900: {0} voltage={1} keypad={2}",
                        passed ? "PASS" : "FAIL", voltagePassed, keypadPassed);
                }
            }
            allPassed &= RunCasinoSyntheticMultiResolution();
            allPassed &= RunNegativeScreenTests();
            Environment.ExitCode = allPassed ? 0 : 1;
        }

        private static bool RunNegativeScreenTests()
        {
            FingerprintDatabase database = new FingerprintDatabase();
            database.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates"));
            using (Bitmap blank = new Bitmap(1920, 1080))
            {
                using (Graphics graphics = Graphics.FromImage(blank)) graphics.Clear(Color.FromArgb(8, 12, 18));
                CayoVoltageResult voltage;
                CasinoKeypadResult keypad;
                CayoFingerprintResult cayo;
                FingerprintTarget target;
                List<int> slots;
                double confidence;
                bool passed = !CayoVoltageScanner.ScanBitmap(blank, out voltage)
                    && !CasinoKeypadScanner.ScanPattern(blank, out keypad)
                    && !CayoFingerprintScanner.ScanBitmap(blank, out cayo)
                    && !ReliableAutoScanner.ScanBitmap(database, blank, out target, out slots, out confidence);
                Console.WriteLine("negative blank screen: " + (passed ? "PASS" : "FAIL"));
                return passed;
            }
        }

        private static bool RunCasinoSyntheticMultiResolution()
        {
            FingerprintDatabase database = new FingerprintDatabase();
            database.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates"));
            int[] expectedSlots = new int[] { 1, 2, 4, 5 };
            bool allPassed = true;
            using (Bitmap source = BuildCasinoFixture(1, expectedSlots, 3))
            {
                string[,] cases = MultiResolutionCases();
                for (int index = 0; index < cases.GetLength(0); index++)
                {
                    int width = Int32.Parse(cases[index, 0]);
                    int height = Int32.Parse(cases[index, 1]);
                    string layout = cases[index, 2];
                    using (Bitmap frame = RenderLayout(source, width, height, layout))
                    {
                        FingerprintTarget target;
                        List<int> slots;
                        double confidence;
                        bool detected = ReliableAutoScanner.ScanBitmap(database, frame, out target, out slots, out confidence);
                        int cursor = ReliableAutoScanner.DetectCursorSlot(frame);
                        bool passed = detected && target != null && target.Id == 1
                            && Same(slots, expectedSlots) && cursor == 3;
                        allPassed &= passed;
                        Console.WriteLine("casino {0}x{1} {2}: {3} target={4} slots=[{5}] cursor={6} score={7:F3}",
                            width, height, layout, passed ? "PASS" : "FAIL",
                            target == null ? 0 : target.Id,
                            slots == null ? "" : string.Join(",", slots.ConvertAll(value => value.ToString()).ToArray()),
                            cursor, confidence);
                    }
                }
            }
            return allPassed;
        }

        private static string[,] MultiResolutionCases()
        {
            return new string[,]
            {
                { "1280", "720", "stretch" },
                { "1600", "900", "stretch" },
                { "1920", "1200", "width-top" },
                { "2560", "1080", "fit-center" },
                { "3440", "1440", "fit-center" },
                { "3840", "2160", "stretch" }
            };
        }

        private static Bitmap BuildVoltageFixture()
        {
            Bitmap voltage = new Bitmap(1920, 1080);
            using (Graphics graphics = Graphics.FromImage(voltage)) graphics.Clear(Color.Black);
            DrawVoltageDigit(voltage, 1, new int[] { 865, 849, 881, 865, 849, 881, 865 }, new int[] { 123, 137, 137, 154, 173, 173, 195 });
            DrawVoltageDigit(voltage, 0, new int[] { 955, 939, 971, 955, 939, 971, 955 }, new int[] { 123, 137, 137, 154, 173, 173, 195 });
            DrawVoltageDigit(voltage, 0, new int[] { 1043, 1029, 1061, 1043, 1029, 1061, 1043 }, new int[] { 123, 137, 137, 154, 173, 173, 195 });
            int[] leftX = new int[] { 509, 495, 527, 509, 495, 527, 509 };
            DrawVoltageDigit(voltage, 8, leftX, new int[] { 271, 287, 287, 303, 323, 323, 343 });
            DrawVoltageDigit(voltage, 7, leftX, new int[] { 507, 522, 522, 540, 557, 557, 579 });
            DrawVoltageDigit(voltage, 6, leftX, new int[] { 741, 755, 755, 773, 791, 791, 813 });
            DrawPoint(voltage, 1349, 277, Color.White); // x10
            DrawPoint(voltage, 1351, 541, Color.White); // x2
            return voltage;
        }

        private static Bitmap BuildKeypadFixture(int[] expected, int ringColumn, int ringRow)
        {
            Bitmap keypad = new Bitmap(1920, 1080);
            using (Graphics graphics = Graphics.FromImage(keypad)) graphics.Clear(Color.Black);
            int[] xs = new int[] { 504, 612, 720, 828, 936, 1044 };
            int[] ys = new int[] { 302, 410, 518, 626, 734 };
            for (int column = 0; column < xs.Length; column++)
                DrawPoint(keypad, xs[column], ys[expected[column] - 1], Color.FromArgb(20, 220, 235));
            using (Graphics graphics = Graphics.FromImage(keypad))
            using (Pen pen = new Pen(Color.White, 9))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = 499 + (ringColumn - 1) * 108;
                int cy = 343 + (ringRow - 1) * 107;
                graphics.DrawEllipse(pen, cx - 50, cy - 50, 100, 100);
            }
            return keypad;
        }

        private static Bitmap BuildCasinoFixture(int target, int[] slots, int cursorSlot)
        {
            Bitmap frame = new Bitmap(1920, 1080);
            using (Graphics graphics = Graphics.FromImage(frame)) graphics.Clear(Color.Black);
            Rectangle[] rectangles = ReliableAutoScanner.GetSlotRectangles(frame);
            for (int part = 0; part < 4; part++)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates",
                    "target_" + target + "_slice_" + (part + 1) + ".png");
                using (Image image = Image.FromFile(path))
                using (Graphics graphics = Graphics.FromImage(frame))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    Rectangle slot = rectangles[slots[part] - 1];
                    int width = (int)Math.Round(slot.Width * 31.0 / 40.0);
                    int height = (int)Math.Round(slot.Height * 33.0 / 39.0);
                    Rectangle destination = new Rectangle(
                        slot.X + (slot.Width - width) / 2,
                        slot.Y + (slot.Height - height) / 2,
                        width, height);
                    graphics.DrawImage(image, destination);
                }
            }
            using (Graphics graphics = Graphics.FromImage(frame))
            using (Pen pen = new Pen(Color.White, 5))
            {
                Rectangle cursor = Rectangle.Inflate(rectangles[cursorSlot - 1], 11, 11);
                graphics.DrawRectangle(pen, cursor);
            }
            return frame;
        }

        private static Bitmap RenderLayout(Bitmap source, int width, int height, string layout)
        {
            Bitmap result = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                Rectangle destination;
                if (layout == "stretch") destination = new Rectangle(0, 0, width, height);
                else
                {
                    double scale = layout == "width-top" ? width / 1920.0 : Math.Min(width / 1920.0, height / 1080.0);
                    int w = (int)Math.Round(1920 * scale);
                    int h = (int)Math.Round(1080 * scale);
                    int x = (width - w) / 2;
                    int y = layout == "width-top" ? 0 : (height - h) / 2;
                    destination = new Rectangle(x, y, w, h);
                }
                graphics.DrawImage(source, destination, new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            }
            return result;
        }

        private static Bitmap AdjustVisual(Bitmap source, float brightness, int offset)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            float shift = offset / 255.0f;
            ColorMatrix matrix = new ColorMatrix(new float[][]
            {
                new float[] { brightness, 0, 0, 0, 0 },
                new float[] { 0, brightness, 0, 0, 0 },
                new float[] { 0, 0, brightness, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { shift, shift, shift, 0, 1 }
            });
            using (ImageAttributes attributes = new ImageAttributes())
            using (Graphics graphics = Graphics.FromImage(result))
            {
                attributes.SetColorMatrix(matrix);
                graphics.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        private static readonly int[][] VoltageDigitPatterns = new int[][]
        {
            new int[] { 1, 1, 1, 0, 1, 1, 1 }, new int[] { 0, 0, 1, 0, 0, 1, 0 },
            new int[] { 1, 0, 1, 1, 1, 0, 1 }, new int[] { 1, 0, 1, 1, 0, 1, 1 },
            new int[] { 0, 1, 1, 1, 0, 1, 0 }, new int[] { 1, 1, 0, 1, 0, 1, 1 },
            new int[] { 1, 1, 0, 1, 1, 1, 1 }, new int[] { 1, 0, 1, 0, 0, 1, 0 },
            new int[] { 1, 1, 1, 1, 1, 1, 1 }, new int[] { 1, 1, 1, 1, 0, 1, 1 }
        };

        private static void DrawVoltageDigit(Bitmap image, int digit, int[] xs, int[] ys)
        {
            for (int i = 0; i < 7; i++) if (VoltageDigitPatterns[digit][i] == 1) DrawPoint(image, xs[i], ys[i], Color.White);
        }

        private static void DrawPoint(Bitmap image, int x, int y, Color color)
        {
            using (Graphics graphics = Graphics.FromImage(image))
            using (Brush brush = new SolidBrush(color))
                graphics.FillRectangle(brush, x - 10, y - 10, 21, 21);
        }

        private static bool SameArray(int[] actual, int[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
            return true;
        }

        private static bool Same(List<int> actual, int[] expected)
        {
            if (actual == null || actual.Count != expected.Length) return false;
            for (int index = 0; index < expected.Length; index++)
            {
                if (actual[index] != expected[index]) return false;
            }
            return true;
        }
    }
}
