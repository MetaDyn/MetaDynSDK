using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MetaDyn.Audio
{
    public static class AudioUtils
    {
        private const int HEADER_SIZE = 44;

        /// <summary>
        /// Converts an AudioClip to a WAV byte array.
        /// </summary>
        public static byte[] EncodeToWAV(AudioClip clip)
        {
            if (clip == null) return null;

            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    var sampleRate = clip.frequency;
                    var channels = (short)clip.channels;
                    var samplesCount = samples.Length;
                    var bitsPerSample = (short)16;
                    var byteRate = sampleRate * channels * (bitsPerSample / 8);
                    var blockAlign = (short)(channels * (bitsPerSample / 8));

                    // RIFF Header
                    writer.Write(Encoding.UTF8.GetBytes("RIFF"));
                    writer.Write(36 + samplesCount * 2); // File size - 8
                    writer.Write(Encoding.UTF8.GetBytes("WAVE"));

                    // fmt Chunk
                    writer.Write(Encoding.UTF8.GetBytes("fmt "));
                    writer.Write(16); // Chunk size
                    writer.Write((short)1); // Audio format (1 = PCM)
                    writer.Write(channels);
                    writer.Write(sampleRate);
                    writer.Write(byteRate);
                    writer.Write(blockAlign);
                    writer.Write(bitsPerSample);

                    // data Chunk
                    writer.Write(Encoding.UTF8.GetBytes("data"));
                    writer.Write(samplesCount * 2); // Data size (samples * 2 bytes per sample)

                    // Write Sample Data
                    RescaleAndWrite(writer, samples);
                    
                    return stream.ToArray();
                }
            }
        }

        private static void RescaleAndWrite(BinaryWriter writer, float[] samples)
        {
            // Convert float (-1..1) to short (-32768..32767)
            for (int i = 0; i < samples.Length; i++)
            {
                float s = samples[i];
                // Clamp to ensure we don't wrap around
                if (s > 1.0f) s = 1.0f;
                if (s < -1.0f) s = -1.0f;
                
                short shortSample = (short)(s * 32767);
                writer.Write(shortSample);
            }
        }
    }
}