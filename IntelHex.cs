using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Megamind.Num.Encode;


namespace Megamind.IO.FileFormat
{
    #region Enumeration and Struct

    public enum RecordType
    {
        DATA = 0,   // Contains data and a 16-bit starting address for the data
        EOF = 1,    // End Of File
        ESA = 2,    // Extended Segment Address
        SSA = 3,    // Start Segment Address
        ELA = 4,    // Extended Linear Address
        SLA = 5     // Start Linear Address
    };

    public class IhexRecord
    {
        public byte DataCount { get; set; }      // 1 byte
        public int Address { get; set; }      // 2 bytes
        public byte RecordType { get; set; }     // 1 byte
        public byte[] DataBlock { get; set; }    // DataCount bytes
        public byte Checksum { get; set; }       // 1 byte
    }

    public class MemoryBlock
    {
        public int Start { get; set; }
        public int End { get; set; }
        public byte[] Data { get; set; }
        public int Size
        {
            get { return End - Start; }
        }

        public MemoryBlock(int start = 0, int end = 0)
        {
            Start = start;
            End = end;
        }
    }

    #endregion

    public class IntelHex
    {
        #region Constants

        private const int RecordStart = 1;
        private const int HexCharPerByte = 2;
        private const int RecordInfoSize = 8;
        private const char RecordStartChar = ':';


        #endregion

        #region Variables and Properties

        public List<IhexRecord> Records { get; private set; }
        public List<MemoryBlock> MemBlocks { get; private set; }

        public string Source { get; private set; }
        public bool BigIndian { get; private set; }
        public int BytesPerRecord { get; set; }

        public int DataLength
        {
            get
            {
                return MemBlocks.Sum(memBlock => memBlock.Data.Length);
            }
        }

        public byte[] RawData
        {
            get
            {
                var data = new List<byte>();
                foreach (var m in MemBlocks)
                    data.AddRange(m.Data);
                return data.ToArray();
            }
        }

        #endregion

        #region Constuctors

        public IntelHex(string filename = "", bool bigIndian = false)
        {
            Source = filename;
            BigIndian = bigIndian;
            BytesPerRecord = 16;
            Records = new List<IhexRecord>();
            MemBlocks = new List<MemoryBlock>();
        }

        #endregion

        #region Parsing records

        public void Read(bool verifyChecksum = true)
        {
            this.Read(this.Source, verifyChecksum);
        }

        public void Read(string filename, bool verifyChecksum = true)
        {
            Source = filename;

            // parse records
            var lines = File.ReadAllText(Source).Split('\n');
            var linecount = 0;
            Records.Clear();

            foreach (var line in lines)
            {
                try
                {
                    linecount++;
                    if (line.Length < RecordStart) continue;
                    if (line.Length < (RecordStart + RecordInfoSize))
                        throw new Exception("Invalid record line length is too small");
                    if (line[0] != RecordStartChar)
                        throw new Exception("Could not find starting RecordStartChar(:) in this reord");

                    var r = ToiRecord(line);
                    if (verifyChecksum)
                    {
                        var chksm = CalculateChecksum(line.Substring(RecordStart, RecordInfoSize + (r.DataCount * HexCharPerByte)));
                        if (r.Checksum != chksm)
                            throw new Exception(string.Format("Checksum byte does not match of the record: {0} Checksum:{1}", line, chksm.ToString("X2")));
                    }

                    Records.Add(r);
                    if (r.RecordType == (byte)RecordType.EOF)
                        break;
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format("Record parsing error at line {0} \r\n{1}", linecount, ex.Message));
                }

                if (Records.Count == 0) throw new Exception("No valid record found on this file");
            }

            // parse memory blocks
            int offset = 0;
            var dataStarted = false;
            var newElaStarted = false;
            var mBlock = new MemoryBlock();
            var bData = new List<byte>();
            MemBlocks.Clear();

            foreach (var r in Records)
            {
                if (r.RecordType == (byte)RecordType.DATA) //0, data bytes
                {
                    int absIndex = offset + r.Address;
                    if (!dataStarted)
                    {
                        mBlock.Start = absIndex;
                        mBlock.End = absIndex;
                        dataStarted = true;
                    }

                    if (absIndex != mBlock.End) // address gap, from 2nd new memory block
                    {
                        mBlock.Data = bData.ToArray();
                        if (mBlock.Data.Length != mBlock.Size)
                            throw new Exception("Memory address block and data length does not match");
                        MemBlocks.Add(mBlock);
                        mBlock = new MemoryBlock(absIndex);
                        bData.Clear();
                    }
                    mBlock.End = absIndex + r.DataCount;
                    bData.AddRange(r.DataBlock);
                }
                else if (r.RecordType == (byte)RecordType.EOF) //1, end of file
                {
                    break; // finish parsing records
                }
                else if (r.RecordType == (byte)RecordType.ESA) //2, segment number, ELA 32 bit address LSB
                {
                    var esaOffset = MU.GetInt16(r.DataBlock[0], r.DataBlock[1]) * 16;
                    if (newElaStarted)
                    {
                        offset += esaOffset;
                        newElaStarted = false;
                    }
                    else offset = esaOffset;
                }
                else if (r.RecordType == (byte)RecordType.ELA) //4, ELA 32 bit address MSB
                {
                    offset = MU.GetInt16(r.DataBlock[0], r.DataBlock[1]) << 16;
                    newElaStarted = true;
                }
            }

            // EOF found or record parsing completed, append last memory block  
            mBlock.Data = bData.ToArray();
            if (mBlock.Data.Length != mBlock.Size)
                throw new Exception("Memory address block and data length does not match");
            MemBlocks.Add(mBlock);
        }

        public void SaveIntelHex(string filename)
        {
            var usingEla = false;
            var prevBlockEnd = 0;
            var memBlockCount = 0;
            var lines = new List<string>();
            foreach (var memBlock in MemBlocks)
            {
                memBlockCount++;
                if (memBlock.Start > 0xFFFF) usingEla = true;
                var isLastMemBlock = (memBlockCount == MemBlocks.Count);
                var resetEla = usingEla && (memBlockCount > 1) && (memBlock.Start <= 0xFFFF) && (memBlock.Start != prevBlockEnd);
                var records = GenearateRecords(memBlock.Data, memBlock.Start, resetEla, isLastMemBlock, BytesPerRecord);
                lines.AddRange(records);
                prevBlockEnd = memBlock.End;
            }
            File.WriteAllLines(filename, lines);
        }

        public void SaveBinaryImage(string filename)
        {
            var bytes = new List<byte>();
            foreach (var memBlock in MemBlocks)
            {
                bytes.AddRange(memBlock.Data);
            }

            File.WriteAllBytes(filename, bytes.ToArray());
        }

        public static IEnumerable<string> GenearateRecords(byte[] dataArr, long startAddress, bool resetElaOffset = false, bool appendEof = true, int bytesPerRecord = 16)
        {
            var checkSum = 255;
            int byteCount = 0;
            long offset = startAddress;
            var records = new List<string>();

            string recordline;
            int elaOffsetMsb;
            // ELA offset set to 0
            if (resetElaOffset)
            {
                elaOffsetMsb = 0;
                recordline = string.Format(":02000004{0:X4}{1:X2}", elaOffsetMsb, checkSum);
                ReplaceRecordChecksum(ref recordline);
                records.Add(recordline);
            }

            while (byteCount < dataArr.Length)
            {
                // greater than 16 bit address
                if (offset > 0xFFFF)
                {
                    elaOffsetMsb = (int)(offset >> 16);   // 32 bit adddress msb
                    recordline = string.Format(":02000004{0:X4}{1:X2}", elaOffsetMsb, checkSum);
                    ReplaceRecordChecksum(ref recordline);
                    records.Add(recordline);
                    offset = (int)(offset & 0xFFFF);    // 32 bit address lsb;
                }

                var dataToCopy = bytesPerRecord;
                if (byteCount + dataToCopy > dataArr.Length) dataToCopy = dataArr.Length - byteCount;
                var data = new byte[dataToCopy];
                Array.Copy(dataArr, byteCount, data, 0, dataToCopy);
                var strdatabytes = BitConverter.ToString(data).Replace("-", "");
                recordline = string.Format(":{0:X2}{1:X4}00{2}{3:X2}", dataToCopy, offset, strdatabytes, checkSum);
                ReplaceRecordChecksum(ref recordline);
                records.Add(recordline);

                offset += dataToCopy;
                byteCount += dataToCopy;
            }

            if (appendEof) records.Add(":00000001FF");
            return records;
        }

        #endregion

        #region Parsing helper

        private IhexRecord ToiRecord(string line)
        {
            var r = new IhexRecord
            {
                DataCount = Convert.ToByte(line.Substring(1, 2), 16),    // 1st 2 char
                Address = Convert.ToUInt16(line.Substring(3, 4), 16),    // next 4 char
                RecordType = Convert.ToByte(line.Substring(7, 2), 16)    // next 2 char
            };

            var datacharcount = r.DataCount * 2;                        // each 2 char contains 1 byte data
            var hexdata = line.Substring(9, datacharcount);             // upto DataCount bytes  
            if (BigIndian) hexdata = ToBigIndian(hexdata);
            r.DataBlock = MU.HexStringToByteArray(hexdata);
            r.Checksum = Convert.ToByte(line.Substring(9 + datacharcount, 2), 16);  // last 2 char

            return r;
        }

        public static string ToBigIndian(string datablock)
        {
            var str = "";
            for (int i = 0; i < datablock.Length; i += 4)
            {
                if ((i + 4) >= datablock.Length) throw new Exception("Not enough data for convert to BigIndian");
                var block = datablock.Substring(i, 4);
                str += block[2] + block[3] + block[0] + block[1];
            }
            return str;
        }

        public static byte CalculateChecksum(string hexdata)
        {
            byte[] buf = MU.HexStringToByteArray(hexdata);
            int chksm = buf.Aggregate(0, (s, b) => s += b) & 0xff;
            chksm = (0x100 - chksm) & 0xff;
            return (byte)chksm;
        }

        public static void ReplaceRecordChecksum(ref string recordline)
        {
            var data = recordline.Substring(1, recordline.Length - 3);
            var csum = CalculateChecksum(data).ToString("X2");
            var newhexline = ":" + data + csum;
            if (recordline.Length != newhexline.Length)
                throw new Exception("Error in Replacing Checksum");
            recordline = newhexline;
        }

        #endregion
    }
}
