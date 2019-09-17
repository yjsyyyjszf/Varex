using System;
using System.Linq;
using XrpadDetector;

namespace XrpadDetectorTest
{
    static class Program
    {
        static void Main()
        {
            var info = Detector.GetInfos().FirstOrDefault();
            if (info != null)
            {
                Console.WriteLine($"Found detector {info.Name} on {info.IP}");
                TestDetector(info);
            }
            else
            {
                Console.WriteLine("Detector not found");
            }

            Console.WriteLine("Press any key...");
            Console.ReadLine();
        }

        private static void TestDetector(DetectorInfo info)
        {
            using (var detector = new Detector(info.Name))
            {
                detector.Acquired += Detector_Acquired;
                detector.ImageReady += Detector_ImageReady;
                detector.SetAedMode(false);
                detector.SetAcquisitionTime(5000);
                detector.StartOffsetCalibration(3);
                Console.ReadLine();
                detector.StartGainCalibration(5);
                Console.ReadLine();
                detector.StartAcquisition(5);
                Console.ReadLine();
            }
        }

        private static void Detector_Acquired(object sender, EventArgs e)
        {
            Console.WriteLine("Acquired");
        }

        private static void Detector_ImageReady(object sender, ImageEventArgs e)
        {
            Console.WriteLine($"Image {e.Width}x{e.Height} with pitch {e.Pitch} μm is ready");
        }
    }
}
