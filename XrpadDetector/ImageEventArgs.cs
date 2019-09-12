using System;

namespace XrpadDetector
{
    public class ImageEventArgs : EventArgs
    {
        public ImageEventArgs(IntPtr data, int width, int height, int pitch)
        {
            Data = data;
            Width = width;
            Height = height;
            Pitch = pitch;
        }

        public IntPtr Data { get; }

        public int Width { get; }

        public int Height { get; }

        public int Pitch { get; }
    }
}
