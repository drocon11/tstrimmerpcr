/*****************************************************************************
 * Version: 0.1
 * License: GNU GPLv2
 * Authors: vocho
 *****************************************************************************/

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

class tstrimmer
{
    public static void Main(string[] args)
    {
        String input_file = "";
        String output_file = "";
        String trim_fps = "30000/1001";
        String trim_file = "";
        String trim_start_margin = "00:00:01.000";
        String trim_end_margin = "00:00:00.500";
        String seek_start = "";
        String seek_duration = "";
        int chunk_power = 14; // 10:188*1024, 12:188*4096, 14:188*16384, 16:188*65536

        if (args.Length == 0)
        {
            Console.WriteLine("tstrimmer -i in.ts -o out.ts [-trimfps " + trim_fps + "] [-trimfile in.ts.txt] [-trimsm hh:mm:ss.zzz] [-trimem hh:mm:ss.zzz] [-ss hh:mm:ss.zzz] [-t hh:mm:ss.zzz]");
            Console.WriteLine("    -i          input file name");
            Console.WriteLine("    -o          output file name");
            Console.WriteLine("    -trimfps    trim frame rate (defalut: " + trim_fps + ")");
            Console.WriteLine("    -trimfile   trim file name");
            Console.WriteLine("    -trimsm     trim start margin (defalut: " + trim_start_margin + ")");
            Console.WriteLine("    -trimem     trim end margin (defalut: " + trim_end_margin + ")");
            Console.WriteLine("    -ss         seek start time");
            Console.WriteLine("    -t          seek duration");
            Environment.Exit(0);
        }

        FileStream reader = null;
        FileStream writer = null;

        try
        {
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "-i":
                        input_file = args[i + 1];
                        break;
                    case "-o":
                        output_file = args[i + 1];
                        break;
                    case "-trimfps":
                        trim_fps = args[i + 1];
                        break;
                    case "-trimfile":
                        trim_file = args[i + 1];
                        break;
                    case "-trimsm":
                        trim_start_margin = args[i + 1];
                        break;
                    case "-trimem":
                        trim_end_margin = args[i + 1];
                        break;
                    case "-ss":
                        seek_start = args[i + 1];
                        break;
                    case "-t":
                        seek_duration = args[i + 1];
                        break;
                }
            }


            // -i
            reader = new FileStream(input_file, FileMode.Open, FileAccess.Read);

            // -o
            writer = new FileStream(output_file, FileMode.Create, FileAccess.Write);

            // -trimfps
            uint trim_fps_num = 30000;
            uint trim_fps_den = 1001;
            if (trim_fps != "")
            {
                string[] ary = trim_fps.Split(new char[] { '/', ':', ',' });
                if (ary.Length == 2)
                {
                    trim_fps_num = uint.Parse(ary[0]);
                    trim_fps_den = uint.Parse(ary[1]);
                }
            }

            // -trimfile
            List<ulong> trim_pcr_list = new List<ulong>();
            if (trim_file != "")
            {
                string trim_text = File.ReadAllText(trim_file);
                MatchCollection matches = Regex.Matches(trim_text, @"\bTrim\(\s*(?<start>\d+)\s*,\s*(?<end>\d+)\s*\)");
                foreach (Match match in matches)
                {
                    trim_pcr_list.Add(ulong.Parse(match.Groups["start"].Value) * 90000 * trim_fps_den / trim_fps_num % 0x200000000);
                    trim_pcr_list.Add(ulong.Parse(match.Groups["end"  ].Value) * 90000 * trim_fps_den / trim_fps_num % 0x200000000);
                }
            }
            if (trim_pcr_list.Count == 0)
            {
                trim_pcr_list.Add(0);
                trim_pcr_list.Add(0x1FFFFFFFF);
            }

            // -ss
            if (seek_start != "")
            {
                ulong seek_start_pcr = (ulong)(TimeSpan.Parse(seek_start).TotalSeconds * 90000) % 0x200000000;
                while ((0 < trim_pcr_list.Count) && (0 < seek_start_pcr))
                {
                    ulong diff = trim_pcr_list[1] - trim_pcr_list[0];
                    if (seek_start_pcr <= diff)
                    {
                        trim_pcr_list[0] += seek_start_pcr;
                        break;
                    }
                    else
                    {
                        trim_pcr_list.RemoveRange(0, 2);
                        seek_start_pcr -= diff;
                    }
                }
            }

            // -t
            if (seek_duration != "")
            {
                ulong seek_duration_pcr = (ulong)(TimeSpan.Parse(seek_duration).TotalSeconds * 90000) % 0x200000000;
                for (int i = 0; i < trim_pcr_list.Count; i += 2)
                {
                    ulong diff = trim_pcr_list[i + 1] - trim_pcr_list[i];
                    if (seek_duration_pcr <= diff)
                    {
                        trim_pcr_list[i + 1] = trim_pcr_list[i] + seek_duration_pcr;
                        i += 2;
                        if (i < trim_pcr_list.Count)
                        {
                            trim_pcr_list.RemoveRange(i, trim_pcr_list.Count - i);
                        }
                        break;
                    }
                    else
                    {
                        seek_duration_pcr -= diff;
                    }
                }
            }
            if (trim_pcr_list.Count == 0)
            {
                throw new Exception();
            }

            // -trimsm
            ulong trim_start_margin_pcr = 1 * 90000;
            if (trim_start_margin != "")
            {
                trim_start_margin_pcr = (ulong)(TimeSpan.Parse(trim_start_margin).TotalSeconds * 90000) % 0x200000000;
            }

            // -trimem
            ulong trim_end_margin_pcr = 1 * 90000;
            if (trim_end_margin != "")
            {
                trim_end_margin_pcr = (ulong)(TimeSpan.Parse(trim_end_margin).TotalSeconds * 90000) % 0x200000000;
            }

            // fix trim margin
            for (int i = 0; i < trim_pcr_list.Count; i += 2)
            {
                if (trim_start_margin_pcr < trim_pcr_list[i])
                {
                    trim_pcr_list[i] -= trim_start_margin_pcr;
                }
                else
                {
                    trim_pcr_list[i] = 0;
                }
                trim_pcr_list[i + 1] += trim_end_margin_pcr;
            }
            ulong[] trim_pcr_ary = trim_pcr_list.ToArray();

            // detect packet size
            int packet_size = 188;
            {
                byte[] buf = new byte[192 * 4];
                reader.Seek(0, SeekOrigin.Begin);
                if (reader.Read(buf, 0, 192 * 4) != (192 * 4))
                {
                    throw new Exception();
                }
                if ((buf[188 * 0] == 0x47) && (buf[188 * 1] == 0x47) && (buf[188 * 2] == 0x47) && (buf[188 * 3] == 0x47))
                {
                    packet_size = 188;
                }
                else if ((buf[192 * 0 + 4] == 0x47) && (buf[192 * 1 + 4] == 0x47) && (buf[192 * 2 + 4] == 0x47) && (buf[192 * 3 + 4] == 0x47))
                {
                    packet_size = 192;
                }
                else
                {
                    throw new Exception();
                }
            }

            // find first pcr
            ulong first_pcr = ulong.MaxValue;
            long first_pcr_idx = 0;
            reader.Seek(0, SeekOrigin.Begin);
            while (true) {
                byte[] buf = new byte[packet_size];
                if (reader.Read(buf, 0, packet_size) != packet_size)
                {
                    break;
                }
                ulong pcr = get_pcr_base(buf, packet_size);
                if (pcr != ulong.MaxValue)
                {
                    first_pcr = pcr;
                    break;
                }
                first_pcr_idx++;
            }
            if (first_pcr == ulong.MaxValue) {
                throw new Exception();
            }

            // find final pcr
            ulong final_pcr = ulong.MaxValue;
            long final_pcr_idx = (reader.Length / packet_size) - 1;
            reader.Seek(packet_size * final_pcr_idx, SeekOrigin.Begin);
            while (true) {
                byte[] buf = new byte[packet_size];
                if (reader.Read(buf, 0, packet_size) != packet_size)
                {
                    break;
                }
                ulong pcr = get_pcr_base(buf, packet_size);
                if (pcr != ulong.MaxValue)
                {
                    final_pcr = pcr;
                    break;
                }
                reader.Seek(-packet_size * 2, SeekOrigin.Current);
                final_pcr_idx--;
            }
            if (final_pcr == ulong.MaxValue) {
                throw new Exception();
            }

            // detect pcr wraparound
            bool wraparound_flag = false;
            if (final_pcr < first_pcr) {
                final_pcr += 0x200000000;
                wraparound_flag = true;
                Console.WriteLine("detected pcr wraparound");
            }

            // find trim first packet index
            long trim_first_idx = -1;
            long trim_prev_idx = -1;
            {
                ulong trim_first_pcr = first_pcr + trim_pcr_ary[0];
                int chunk_scale = 1 << chunk_power; // 2 ^ 10 = 1024
                //int chunk_size = packet_size * chunk_scale; // 188 * 1024, 192 * 1024
                long base_chunk_idx = (long)(((ulong)first_pcr_idx + (ulong)(final_pcr_idx - first_pcr_idx) * trim_pcr_ary[0] / (final_pcr - first_pcr)) >> chunk_power);
                long base_packet_idx = (long)(base_chunk_idx << chunk_power);
                if (trim_first_pcr == first_pcr)
                {
                    trim_first_idx = first_pcr_idx;
                    trim_prev_idx = first_pcr_idx;
                }
                else
                {
                    reader.Seek(packet_size * base_packet_idx, SeekOrigin.Begin);
                    for (long packet_idx = base_packet_idx; packet_idx <= final_pcr_idx; packet_idx++) {
                        byte[] buf = new byte[packet_size];
                        if (reader.Read(buf, 0, packet_size) != packet_size)
                        {
                            break;
                        }
                        ulong pcr = get_pcr_base(buf, packet_size);
                        if (pcr != ulong.MaxValue)
                        {
                            if (wraparound_flag && (pcr < first_pcr))
                            {
                                pcr += 0x200000000;
                            }
                            if (pcr >= trim_first_pcr)
                            {
                                trim_first_idx = packet_idx;
                                break;
                            }
                            else
                            {
                                trim_prev_idx = packet_idx;
                            }
                        }
                    }
                }
                if (trim_prev_idx < 0)
                {
                    for (long chunk_idx = base_chunk_idx - 1; 0 <= chunk_idx; chunk_idx--) {
                        long seek_packet_idx = chunk_idx * chunk_scale;
                        reader.Seek(packet_size * seek_packet_idx, SeekOrigin.Begin);
                        for (long packet_idx = 0; packet_idx < chunk_scale; packet_idx++) {
                            byte[] buf = new byte[packet_size];
                            if (reader.Read(buf, 0, packet_size) != packet_size)
                            {
                                break;
                            }
                            ulong pcr = get_pcr_base(buf, packet_size);
                            if (pcr != ulong.MaxValue)
                            {
                                if (wraparound_flag && (pcr < first_pcr))
                                {
                                    pcr += 0x200000000;
                                }
                                if (pcr >= trim_first_pcr)
                                {
                                    trim_first_idx = packet_idx + seek_packet_idx;
                                    if (trim_prev_idx >= 0)
                                    {
                                        chunk_idx = 0;
                                    }
                                    break;
                                }
                                else
                                {
                                    trim_prev_idx = packet_idx + seek_packet_idx;
                                    if (trim_first_idx >= 0)
                                    {
                                        chunk_idx = 0;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                if (trim_first_idx < 0) {
                    throw new Exception();
                }
                if (trim_prev_idx < 0) {
                    trim_prev_idx = trim_first_idx;
                }
            }

            bool last_write_status = false;
            bool finished_trim = false;
            int seek_trim_idx = 0;

            // trim
            reader.Seek(packet_size * trim_first_idx, SeekOrigin.Begin);
            while (!finished_trim)
            {
                byte[] buf = new byte[packet_size];
                if (reader.Read(buf, 0, packet_size) != packet_size)
                {
                    break;
                }
                ulong pcr = get_pcr_base(buf, packet_size);
                bool write_status = false;
                if (pcr != ulong.MaxValue)
                {
                    if (wraparound_flag && (pcr < first_pcr))
                    {
                        pcr += 0x200000000;
                    }
                    for (int i = seek_trim_idx; i < trim_pcr_ary.Length; i += 2)
                    {
                        ulong trim_start_pcr = first_pcr + trim_pcr_ary[i];
                        ulong trim_end_pcr = first_pcr + trim_pcr_ary[i + 1];
                        if (trim_start_pcr <= pcr)
                        {
                            if (pcr <= trim_end_pcr)
                            {
                                writer.Write(buf, 0, packet_size);
                                write_status = true;
                                seek_trim_idx = i;
                                break;
                            }
                            else if (seek_trim_idx >= (trim_pcr_ary.Length - 2))
                            {
                                finished_trim = true;
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    if (last_write_status)
                    {
                        writer.Write(buf, 0, packet_size);
                        write_status = true;
                    }
                }
                last_write_status = write_status;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        finally
        {
            if (writer != null)
            {
                writer.Close();
            }
            if (reader != null)
            {
                reader.Close();
            }
        }
    }

    static ulong get_pcr_base(byte[] buf, int packet_size)
    {
        ulong pcr_base = ulong.MaxValue;
        int i = 0;

        if (packet_size == 192)
        {
            i += 4;
        }

        bool sync_byte = (buf[i + 0] == 0x47);
        ushort pid = (ushort)((((ushort)(buf[i + 1] & 0x1F)) << 8) + buf[i + 2]);
        bool adaptation_field_indicator = ((buf[i + 3] & 0x20) == 0x20);
        i += 4;

        if (!sync_byte)
        {
            return ulong.MaxValue;
        }

        if (adaptation_field_indicator)
        {
            int adaptation_field_length = buf[i + 0];
            i += 1;

            if (adaptation_field_length > 183)
            {
                return ulong.MaxValue;
            }
            else if (adaptation_field_length > 0)
            {
                bool pcr_flag = ((buf[i + 0] & 0x10) == 0x10);
                i += 1;

                if (pcr_flag)
                {
                    if ((pid == 0x0000) || (pid == 0x0001) || ((0x0010 <= pid) && (pid <= 0x1FFE)))
                    {
                        pcr_base = (ulong)(((ulong)(buf[i + 0]       )) << 8 * 4 - 7) +
                                   (ulong)(((ulong)(buf[i + 1]       )) << 8 * 3 - 7) +
                                   (ulong)(((ulong)(buf[i + 2]       )) << 8 * 2 - 7) +
                                   (ulong)(((ulong)(buf[i + 3]       )) << 8 * 1 - 7) +
                                   (ulong)(((ulong)(buf[i + 4] & 0x80)) >>         7);
                    }
                }
            }
        }

        return pcr_base;
    }
}


