using System;
using System.Collections.Generic;
using System.Drawing;
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
            bool voltagePassed;
            using (Bitmap voltage = new Bitmap(1920, 1080))
            {
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
                CayoVoltageResult result;
                voltagePassed = CayoVoltageScanner.ScanBitmap(voltage, out result)
                    && result.Target == 100 && SameArray(result.LeftNumbers, new int[] { 8, 7, 6 })
                    && SameArray(result.RightMultipliers, new int[] { 10, 2, 1 })
                    && SameArray(result.Assignment, new int[] { 0, 1, 2 });
            }

            bool keypadPassed;
            using (Bitmap keypad = new Bitmap(1920, 1080))
            {
                using (Graphics graphics = Graphics.FromImage(keypad)) graphics.Clear(Color.Black);
                int[] xs = new int[] { 504, 612, 720, 828, 936, 1044 };
                int[] ys = new int[] { 302, 410, 518, 626, 734 };
                int[] expected = new int[] { 1, 3, 5, 2, 4, 1 };
                for (int column = 0; column < xs.Length; column++) DrawPoint(keypad, xs[column], ys[expected[column] - 1], Color.Cyan);
                CasinoKeypadResult result;
                keypadPassed = CasinoKeypadScanner.ScanPattern(keypad, out result) && SameArray(result.Rows, expected);
            }

            Console.WriteLine("voltage scanner: " + (voltagePassed ? "PASS" : "FAIL"));
            Console.WriteLine("keypad scanner: " + (keypadPassed ? "PASS" : "FAIL"));
            Environment.ExitCode = voltagePassed && keypadPassed ? 0 : 1;
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
