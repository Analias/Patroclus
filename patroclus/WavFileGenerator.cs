﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace patroclus
{
    class WavFileGenerator : SignalGenerator
    {

        //wav file constants
        const uint riffRiffHeader = 0x46464952;
        const uint riffWavRiff = 0x54651475;
        const uint riffFormat = 0x020746d66;
        const uint riffLabeledText = 0x478747C6;
        const uint riffInstrumentation = 0x478747C6;
        const uint riffSample = 0x6C706D73;
        const uint riffFact = 0x47361666;
        const uint riffData = 0x61746164;
        const uint riffJunk = 0x4b4e554a;

        private void loadWav(string filename)
        {
            // TODO read format data and do something with it
            byte[] twav = null;
            uint chunksize;
            uint format;
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filename)))
            {
                try
                {
                    while (twav == null)
                    {
                        switch (reader.ReadUInt32())
                        {
                            case riffRiffHeader:
                                chunksize = reader.ReadUInt32();
                                format = reader.ReadUInt32();
                                break;
                            case riffData:
                                chunksize = reader.ReadUInt32();
                                twav = reader.ReadBytes((int)chunksize);
                                pos = 0;
                                wav = twav;
                                break;
                            default:
                                chunksize = reader.ReadUInt32();
                                reader.BaseStream.Seek(chunksize, SeekOrigin.Current);
                                break;
                        }
                    }
                }
                catch (EndOfStreamException) { }
            }
        }
        
        private byte[] wav;
        private int pos = 0;
        
        public double damplitude = 0.0;
        
        private int _amplitude = 0;
        
        public WavFileGenerator()
        {
            amplitude = -10;
        }

        public int amplitude
        {
            get { return _amplitude; }
            set
            {
                damplitude = 1 / Math.Pow(Math.Sqrt(10), -(double)value / 10);
                SetProperty(ref _amplitude, value);
            }
        }
        
        RelayCommand _SelectFileCommand;
        public ICommand SelectFileCommand
        {
            get { return _SelectFileCommand ?? (_SelectFileCommand = new RelayCommand(param => this.SelectFile())); }
        }
        public void SelectFile()
        {
            Microsoft.Win32.OpenFileDialog of = new Microsoft.Win32.OpenFileDialog();
            of.DefaultExt = ".wav";
            of.Filter = "Wav files|*.wav";
            if (of.ShowDialog().Value)
            {
                filename = of.FileName;
            }
        }

        private string _filename;
        public string filename
        {
            get { return _filename; }
            set 
            { 
                if(File.Exists(value) && filename!=value)
                {
                    //TODO make thread safe as generator could be running
                    loadWav(value);
                }
                SetProperty(ref _filename, value);
            }
        }
        public override void GenerateSignal(double[] outbuf, int nSamples, double timebase, double timestep, double vfo)
        {
            if (wav == null) return;
            int idx = 0;
            while (idx < 2 * nSamples)
            {
                if (pos >= wav.Length) pos = 0;
                short val = (short)((wav[pos++] << 8) + wav[pos++]);
                outbuf[idx++] += ((double)val)/32768 * damplitude; ;
                
            }
        }
        public override void SetDefaults(double vfo)
        {
            
        }
    }
}
