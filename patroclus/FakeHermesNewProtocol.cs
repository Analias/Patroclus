﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

namespace patroclus
{
    public class FakeHermesNewProtocol : FakeRadio
    {
        private udpConnection generalClient;
        private udpConnection rxSpecificClient;
        private udpConnection txSpecificClient;
        private udpConnection highPriorityClient;
        private udpConnection tx0IQClient;
        private udpConnection rxAudioClient;

        const int maxReceivers = 80;

        Dictionary<receiver, UdpClient> rxClients = new Dictionary<receiver, UdpClient>();

        receiver[] receiversByIdx = new receiver[maxReceivers];

        private Thread handleCommsThread;

        public int port { get; set; } //Port for the Client to use


        IPEndPoint ClientIpEndPoint;

        const byte sync = 0x7f;
        const int max24int = 0x7fffff;
        const int min24int = -0x800000;


        byte[] databuf = new byte[1444];
        uint seqNo = 0;
        uint micSeqNo = 0;

        double timebase = 0.0;
        byte hermesCodeVersion = 30;
        DateTime startTime;
        bool running = false;


        private int _RxSpecificPort = 1025;
        public int RxSpecificPort
        {
            get { return _RxSpecificPort; }
            set { SetProperty(ref _RxSpecificPort, value); }
        }

        private int _TxSpecificPort = 1026;
        public int TxSpecificPort
        {
            get { return _TxSpecificPort; }
            set { SetProperty(ref _TxSpecificPort, value); }
        }
        private int _HighPriorityFromPCPort = 1027;
        public int HighPriorityFromPCPort
        {
            get { return _HighPriorityFromPCPort; }
            set { SetProperty(ref _HighPriorityFromPCPort, value); }
        }

        private int _HighPriorityToPCPort = 1025;
        public int HighPriorityToPCPort
        {
            get { return _HighPriorityToPCPort; }
            set { SetProperty(ref _HighPriorityToPCPort, value); }
        }
        private int _ReceiverAudioPort = 1028;
        public int ReceiverAudioPort
        {
            get { return _ReceiverAudioPort; }
            set { SetProperty(ref _ReceiverAudioPort, value); }
        }
        private int _Tx0IQPort = 1029;
        public int Tx0IQPort
        {
            get { return _Tx0IQPort; }
            set { SetProperty(ref _Tx0IQPort, value); }
        }

        private int _Rx0Port = 1035;
        public int Rx0Port
        {
            get { return _Rx0Port; }
            set { SetProperty(ref _Rx0Port, value); }
        }

        private int _MicSamplesPort = 1026;
        public int MicSamplesPort
        {
            get { return _MicSamplesPort; }
            set { SetProperty(ref _MicSamplesPort, value); }
        }

        private int _WidebandADC0Port = 1027;
        public int WidebandADC0Port
        {
            get { return _WidebandADC0Port; }
            set { SetProperty(ref _WidebandADC0Port, value); }
        }
        /*
Wideband Enable [7:0]
Wideband Samples per packet [15:8]
Wideband sample size 
Wideband update rate 
Pure Signal - Rx(n) to use for off air signal
Pure Signal - Rx(n) to use for DAC signal
Pure Signal Sampling Rate [15:8]
Pure Signal Sampling Rate [7:0]
Envelope PWM_max
Envelope PWM_max
Envelope PWM_min
Envelope PWM_min
Bits - [0]Time stamp, [1]VITA-49, [2]VNA mode
*/

        public FakeHermesNewProtocol()
        {
            BindingOperations.CollectionRegistering += BindingOperations_CollectionRegistering;

        }

        void BindingOperations_CollectionRegistering(object sender, CollectionRegisteringEventArgs e)
        {
            BindingOperations.EnableCollectionSynchronization(receivers, _receiversLock);
        }
        private object _receiversLock = new object();

        private ObservableCollection<receiver> _receivers = new ObservableCollection<receiver>();
        public ObservableCollection<receiver> receivers
        {
            get { return _receivers; }
            set { SetProperty(ref _receivers, value); }
        }

        private int _bandwidth = 0;
        public int bandwidth
        {
            get { return _bandwidth; }
            set { SetProperty(ref _bandwidth, value); }
        }

        private int _txNCO = 0;
        public int txNCO
        {
            get { return _txNCO; }
            set { SetProperty(ref _txNCO, value); }
        }

        private bool _duplex = false;
        public bool duplex
        {
            get { return _duplex; }
            set { SetProperty(ref _duplex, value); }
        }
        private bool _adc1clip = false;
        public bool adc1clip
        {
            get { return _adc1clip; }
            set { SetProperty(ref _adc1clip, value); }
        }

        private string _status = "Off";
        public string status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }
        private int _packetsSent = 0;
        public int packetsSent
        {
            get { return _packetsSent; }
            set { SetProperty(ref _packetsSent, value); }
        }
        private int _packetsReceived = 0;
        public int packetsReceived
        {
            get { return _packetsReceived; }
            set { SetProperty(ref _packetsReceived, value); }
        }

        public void start()
        {
            generalClient = new udpConnection() { Client = connect(port), msgQueue = new ConcurrentQueue<receivedPacket>() };
            generalClient.Client.BeginReceive(new AsyncCallback(incomming), generalClient);

            rxSpecificClient = new udpConnection() { Client = connect(RxSpecificPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            rxSpecificClient.Client.BeginReceive(new AsyncCallback(incomming), rxSpecificClient);

            txSpecificClient = new udpConnection() { Client = connect(TxSpecificPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            txSpecificClient.Client.BeginReceive(new AsyncCallback(incomming), txSpecificClient);

            highPriorityClient = new udpConnection() { Client = connect(HighPriorityFromPCPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            highPriorityClient.Client.BeginReceive(new AsyncCallback(incomming), highPriorityClient);

            tx0IQClient = new udpConnection() { Client = connect(Tx0IQPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            tx0IQClient.Client.BeginReceive(new AsyncCallback(incomming), tx0IQClient);

            rxAudioClient = new udpConnection() { Client = connect(ReceiverAudioPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            rxAudioClient.Client.BeginReceive(new AsyncCallback(incomming), rxAudioClient);


            handleCommsThread = new Thread(handleComms);
            handleCommsThread.IsBackground = true;
            handleCommsThread.Start();

        }
        private UdpClient connect(int port)
        {
            UdpClient client = new UdpClient(port);

            const int SIO_UDP_CONNRESET = -1744830452;
            byte[] inValue = new byte[] { 0 };
            byte[] outValue = new byte[] { 0 };
            client.Client.IOControl(SIO_UDP_CONNRESET, inValue, outValue);

            return client;
        }
        public override void Stop()
        {
            handleCommsThread.Abort();
            generalClient.Client.Close();
            rxSpecificClient.Client.Close();
            txSpecificClient.Client.Close();
            highPriorityClient.Client.Close();
            tx0IQClient.Client.Close();
            rxAudioClient.Client.Close();
            //TODO rest of cleanup

            base.Stop();
        }
        long actualPacketCount = 0;
        void handleComms()
        {
            while (true)
            {
                while (!generalClient.msgQueue.IsEmpty)
                {
                    receivedPacket packet;
                    if (generalClient.msgQueue.TryDequeue(out packet)) handleGeneralPacket(packet);
                }
                while (!rxSpecificClient.msgQueue.IsEmpty)
                {
                    receivedPacket packet;
                    if (rxSpecificClient.msgQueue.TryDequeue(out packet)) handleRxSpecificPacket(packet);
                }
                while (!txSpecificClient.msgQueue.IsEmpty)
                {
                    receivedPacket packet;
                    if (txSpecificClient.msgQueue.TryDequeue(out packet)) handleTxSpecificPacket(packet);
                }
                while (!highPriorityClient.msgQueue.IsEmpty)
                {
                    receivedPacket packet;
                    if (highPriorityClient.msgQueue.TryDequeue(out packet)) handleHighPriorityPacket(packet);
                }
                while (!tx0IQClient.msgQueue.IsEmpty)
                {
                    receivedPacket packet;
                    if (tx0IQClient.msgQueue.TryDequeue(out packet)) handleTxIQPacket(packet, 0);
                }
                while (!rxAudioClient.msgQueue.IsEmpty)
                {
                    receivedPacket packet;
                    if (rxAudioClient.msgQueue.TryDequeue(out packet)) handleRXAudioPacket(packet);
                }
                //send any output
                if (running)
                {
                    int samples = 240;

                    adc1clip = false;
                    int channels = receivers.Count();
                    int nSamples = samples;
                    double timeStep = 1.0 / bandwidth;


                    //calculate number of packets to maintain sync
                    DateTime now = DateTime.Now;
                    long totalTime = (long)(now - startTime).TotalMilliseconds;
                    long nPacketsCalculated = bandwidth / (nSamples) * totalTime / 1000;

                    long packetsToSend = nPacketsCalculated - actualPacketCount;



                    for (int i = 0; i < packetsToSend; i++)
                    {
                        for (int ri = 0; ri < receivers.Count; ri++)
                        {
                            int seqNo = receivers[ri].seq;
                            receivers[ri].seq++;
                            //sequence no
                            databuf[0] = (byte)(seqNo >> 24);
                            databuf[1] = (byte)((seqNo >> 16) & 0xff);
                            databuf[2] = (byte)((seqNo >> 8) & 0xff);
                            databuf[3] = (byte)(seqNo & 0xff);
                            /*     //timestamp
                                 databuf[4] = (byte)(seqNo & 0xff);
                                 databuf[5] = (byte)(seqNo & 0xff);
                                 databuf[6] = (byte)(seqNo & 0xff);
                                 databuf[7] = (byte)(seqNo & 0xff);
                                 databuf[8] = (byte)(seqNo & 0xff);
                                 databuf[9] = (byte)(seqNo & 0xff);
                                 databuf[10] = (byte)(seqNo & 0xff);
                                 databuf[11] = (byte)(seqNo & 0xff);
                                 //no of samples
                                 databuf[12] = (byte)(samples>>8);
                                 databuf[13] = (byte)(samples & 0xff);
                            */
                            //samples



                            //    receivers[ri].GenerateSignal(databuf, 16, 6, samples, timebase, timeStep);
                            receivers[ri].GenerateSignal(databuf, 4, 6, samples, timebase, timeStep);
                            rxClients[receivers[ri]].Send(databuf, databuf.Length, ClientIpEndPoint);

                        }

                        //seqNo++;
                        actualPacketCount++;
                        packetsSent++;
                        timebase += nSamples * timeStep;
                    }
                    //  if (highPriorityToPC != null) 
                    sendHighPriorityToPC();

                    long nMicPacketsCalculated = 48000 / 512 * totalTime / 1000;
                    uint micPacketsToSend = ((uint)nMicPacketsCalculated) - micSeqNo;
                    for (int i = 0; i < micPacketsToSend; i++) sendMicData();
                }


                Thread.Sleep(1);
            }
        }
        byte[] hpbuf = new byte[55];

        void sendHighPriorityToPC()
        {
            hpbuf[0] = (byte)(seqNo >> 24);
            hpbuf[1] = (byte)((seqNo >> 16) & 0xff);
            hpbuf[2] = (byte)((seqNo >> 8) & 0xff);
            hpbuf[3] = (byte)(seqNo & 0xff);

            hpbuf[5] = adc1clip ? (byte)1 : (byte)0;
            generalClient.Client.Send(hpbuf, hpbuf.Length, ClientIpEndPoint);
            seqNo++;

        }
        byte[] micbuf = new byte[1444];

        void sendMicData()
        {

            micbuf[0] = (byte)(micSeqNo >> 24);
            micbuf[1] = (byte)((micSeqNo >> 16) & 0xff);
            micbuf[2] = (byte)((micSeqNo >> 8) & 0xff);
            micbuf[3] = (byte)(micSeqNo & 0xff);

            txSpecificClient.Client.Send(micbuf, micbuf.Length, ClientIpEndPoint);
            micSeqNo++;

        }

        private void incomming(IAsyncResult res)
        {
            udpConnection con = res.AsyncState as udpConnection;

            IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, port);
            byte[] received = null;
            packetsReceived++;
            try
            {
                received = con.Client.EndReceive(res, ref RemoteIpEndPoint);
            }
            catch (Exception)
            {

            }
            try
            {
                con.Client.BeginReceive(new AsyncCallback(incomming), con);
                if (received != null)
                {
                    con.msgQueue.Enqueue(new receivedPacket() { received = received, endPoint = RemoteIpEndPoint });
                }
            }
            catch (Exception)
            {
            }
        }

        IPAddress udpBroadcast = new IPAddress(new byte[] { 255, 255, 255, 255 });
        public void handleGeneralPacket(receivedPacket packet)
        {
            //     Console.Out.WriteLine("gp");
            byte[] received = packet.received;
            if (packet.endPoint.Address.Equals(udpBroadcast))
            {

            }
            //old style discovery
            else if (received[0] == 0xef && received[1] == 0xfe && received[2] == 2)
            {
                //discovery
                byte[] response = new byte[60];
                response[0] = 0xef;
                response[1] = 0xfe;
                response[2] = 0x02;
                //add mac address - kiss does not like blank one
                response[3] = 0x00;
                response[4] = 0x00;
                response[5] = 0x00;
                response[6] = 0x00;
                response[7] = 0x00;
                response[8] = 0x01;

                response[9] = hermesCodeVersion;//code version
                response[10] = 0x01;//board type
                status = "Discovered";
                seqNo = 1;
                generalClient.Client.Send(response, response.Length, packet.endPoint);
                packetsSent++;

                ClientIpEndPoint = packet.endPoint;
            }
            else if (received[6] == 0)
            {
                RxSpecificPort = (received[7] << 8) + received[8];
                TxSpecificPort = (received[9] << 8) + received[10];

                HighPriorityFromPCPort = (received[13] << 8) + received[14];
                //   if (highPriorityToPC == null) highPriorityToPC = new UdpClient(HighPriorityToPCPort);

                Rx0Port = (received[19] << 8) + received[20];



            }


        }
        private void handleRxSpecificPacket(receivedPacket packet)
        {
            Console.Out.WriteLine("rxsp");
            int nReceivers = 0;
            byte[] received = packet.received;

            int adcs = received[4];


            for (int f = 0; f < 10; f++)
            {
                int mask = 1;
                for (int i = 0; i < 8; i++)
                {
                    int idx = f * 8 + i;
                    if ((received[7 + f] & mask) != 0)
                    {
                        nReceivers++;
                        if (receiversByIdx[idx] == null)
                        {
                            receiversByIdx[idx] = new receiver("RX" + idx);
                            lock (_receiversLock)
                            {
                                receivers.Add(receiversByIdx[idx]);
                            }
                            rxClients.Add(receiversByIdx[idx], new UdpClient(Rx0Port + idx));
                        }
                    }
                    else
                    {
                        if (receiversByIdx[idx] != null)
                        {
                            lock (_receiversLock)
                            {
                                receivers.Remove(receiversByIdx[idx]);
                                receiversByIdx[idx] = null;
                            }
                        }
                    }
                    if (receiversByIdx[idx] != null)
                    {
                        int srate = (received[18 + idx * 6] << 8) + received[19 + idx * 6];
                        receiversByIdx[idx].bandwidth = srate * 1000;
                    }

                    mask <<= 1;
                }
            }
        }
        private void handleRXAudioPacket(receivedPacket packet)
        {
            //   Console.Out.WriteLine("txsp");
        }
        private void handleTxSpecificPacket(receivedPacket packet)
        {
            //    Console.Out.WriteLine("txsp");
        }
        private void handleTxIQPacket(receivedPacket packet, int adc)
        {
            //    Console.Out.WriteLine("txiq");
        }
        private void handleHighPriorityPacket(receivedPacket packet)
        {
            byte[] received = packet.received;
            bool run = ((received[4] & 0x01) != 0);
            if (run != running)
            {
                if (run)
                {
                    resetTransmission();
                    running = true;
                    status = "Running";
                }
                else
                {
                    running = false;
                    status = "Off";
                }
            }
            bool ptt0 = ((received[4] & 0x02) != 0);
            if (ptt0)
            {
                //   Console.Out.WriteLine("ptt");
            }
            int rxi = 9;
            for (int i = 0; i < maxReceivers; i++)
            {
                if (receiversByIdx[i] != null)
                {
                    receiversByIdx[i].vfo = (((int)received[rxi]) << 24) + (((int)received[rxi + 1]) << 16) + (((int)received[rxi + 2]) << 8) + (int)received[rxi + 3];

                    receiversByIdx[i].generators[0].SetDefaults(receivers[i].vfo);
                    receiversByIdx[i].generators[1].SetDefaults(receivers[i].vfo + 10000);
                }
                rxi += 4;
            }
            int txi = 329;
            txNCO = (((int)received[txi]) << 24) + (((int)received[txi + 1]) << 16) + (((int)received[txi + 2]) << 8) + (int)received[txi + 3];
        }
        void resetTransmission()
        {
            startTime = DateTime.Now;
            actualPacketCount = 0;
            micSeqNo = 0;
            foreach (receiver rx in receivers) rx.seq = 0;
        }
    }
    public class udpConnection
    {
        public UdpClient Client { get; set; }
        public ConcurrentQueue<receivedPacket> msgQueue { get; set; }
    }
}
