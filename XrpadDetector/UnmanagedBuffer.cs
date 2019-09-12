using System;
using System.IO;
using System.Runtime.InteropServices;

namespace XrpadDetector
{
    class UnmanagedBuffer : IDisposable
    {
        public UnmanagedBuffer(int size)
        {
            Size = size;
            Pointer = Marshal.AllocHGlobal(size);
        }

        public void Dispose()
        {
            if (Pointer != null)
            {
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }

        public int Size { get; }

        public IntPtr Pointer { get; private set; }

        public static implicit operator IntPtr(UnmanagedBuffer buffer) => buffer != null ? buffer.Pointer : IntPtr.Zero;

        public static UnmanagedBuffer Load(string filePath, int headerSize = 0)
        {
            var buffer = new UnmanagedBuffer((int)new FileInfo(filePath).Length - headerSize);
            using (Stream fileStream = File.OpenRead(filePath), bufferStream = buffer.CreateStream(FileAccess.Write))
            {
                fileStream.Position += headerSize;
                fileStream.CopyTo(bufferStream);
            }
            return buffer;
        }

        public void Save(string filePath)
        {
            using (Stream fileStream = File.Create(filePath), bufferStream = CreateStream(FileAccess.Read))
            {
                bufferStream.CopyTo(fileStream);
            }
        }

        unsafe private UnmanagedMemoryStream CreateStream(FileAccess fileAccess) => new UnmanagedMemoryStream((byte*)Pointer, Size, Size, fileAccess);
    }
}
