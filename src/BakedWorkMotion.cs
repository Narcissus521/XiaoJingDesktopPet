using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;
namespace XiaoJingDesktopPet
{
    internal sealed class BakedPairInfo {
        public int from {get;set;} public int to {get;set;} public string file {get;set;} public int count {get;set;}
    }
    internal sealed class BakedWorkProfile {
        public int revision {get;set;} public int width {get;set;} public int height {get;set;}
        public BakedPairInfo[] pairs {get;set;}
    }
    internal sealed class BakedWorkMotion : IDisposable
    {
        private readonly string folder;
        private readonly Dictionary<string,BakedPairInfo> entries=new Dictionary<string,BakedPairInfo>();
        private readonly Dictionary<string,BakedPairClip> clips=new Dictionary<string,BakedPairClip>();
        private readonly List<string> recent=new List<string>();
        internal BakedWorkMotion(string root) {
            folder=Path.Combine(root,"quality");
            var profile=new JavaScriptSerializer().Deserialize<BakedWorkProfile>(File.ReadAllText(Path.Combine(root,"quality-motion.json")));
            if(profile==null||profile.revision!=401||profile.width!=272||profile.height!=208||profile.pairs==null)throw new InvalidDataException("Incomplete quality work animation.");
            foreach(var p in profile.pairs){
                if(p.from<0||p.to>=12||p.from>=p.to||p.count<2||p.file!=Name(p.from,p.to)+".vfi")throw new InvalidDataException("Invalid quality work pair.");
                entries.Add(Name(p.from,p.to),p);
            }
        }
        private static string Name(int a,int b){return Math.Min(a,b).ToString("00")+"-"+Math.Max(a,b).ToString("00");}
        internal void Render(ScenePose pose,double progress,int[][] keys,int[] output) {
            if(pose.From==pose.To||progress<=0){Array.Copy(keys[pose.From],output,output.Length);return;}
            if(progress>=1){Array.Copy(keys[pose.To],output,output.Length);return;}
            string name=Name(pose.From,pose.To);BakedPairClip clip;
            if(!clips.TryGetValue(name,out clip)){
                BakedPairInfo entry;if(!entries.TryGetValue(name,out entry))throw new InvalidDataException("Missing quality work pair "+name);
                clip=new BakedPairClip(Path.Combine(folder,entry.file),entry.count);clips.Add(name,clip);
            }
            recent.Remove(name);recent.Add(name);
            while(recent.Count>4){string old=recent[0];recent.RemoveAt(0);clips[old].Dispose();clips.Remove(old);}
            clip.Sample(pose.From<pose.To?progress:1-progress,output);
        }
        public void Dispose(){foreach(var clip in clips.Values)clip.Dispose();clips.Clear();recent.Clear();}
    }
    internal sealed class BakedPairClip : IDisposable
    {
        private readonly FileStream file;
        private readonly uint[] offsets;
        private readonly long dataStart;
        private readonly int count;
        private readonly int[][] cached={new int[272*208],new int[272*208]};
        private readonly int[] indexes={-1,-1};
        private readonly byte[] raw=new byte[272*208*4],compressed;
        internal BakedPairClip(string path,int expectedCount) {
            file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            try {
                using(var reader=new BinaryReader(file,System.Text.Encoding.UTF8,true)){
                    if(reader.ReadInt32()!=0x31494656||reader.ReadInt32()!=272||reader.ReadInt32()!=208)throw new InvalidDataException("Invalid quality frame clip.");
                    count=reader.ReadInt32();if(count!=expectedCount||count<2||count>257||reader.ReadInt32()!=raw.Length)throw new InvalidDataException("Invalid quality frame count.");
                    offsets=new uint[count+1];uint largest=0;
                    for(int i=0;i<offsets.Length;i++){
                        offsets[i]=reader.ReadUInt32();if(i>0){if(offsets[i]<=offsets[i-1])throw new InvalidDataException("Invalid frame offsets.");largest=Math.Max(largest,offsets[i]-offsets[i-1]);}
                    }
                    dataStart=file.Position;
                    if(offsets[0]!=0||file.Length!=dataStart+offsets[count]||largest>raw.Length+1024)throw new InvalidDataException("Incomplete quality frame clip.");
                    compressed=new byte[largest];
                }
            }catch{file.Dispose();throw;}
        }
        private static void ReadAll(Stream input,byte[] buffer,int count){int at=0;while(at<count){int got=input.Read(buffer,at,count-at);if(got==0)throw new EndOfStreamException();at+=got;}}
        private int[] Frame(int index,int protect) {
            for(int i=0;i<2;i++)if(indexes[i]==index)return cached[i];
            int slot=indexes[0]==protect?1:0;int length=(int)(offsets[index+1]-offsets[index]);
            file.Position=dataStart+offsets[index];ReadAll(file,compressed,length);
            using(var memory=new MemoryStream(compressed,0,length,false))using(var inflate=new DeflateStream(memory,CompressionMode.Decompress)){ReadAll(inflate,raw,raw.Length);}
            Buffer.BlockCopy(raw,0,cached[slot],0,raw.Length);indexes[slot]=index;return cached[slot];
        }
        internal void Sample(double progress,int[] output) {
            double p=Math.Max(0,Math.Min(1,progress))*(count-1);int first=Math.Min(count-1,(int)p),next=Math.Min(count-1,first+1);
            int[] a=Frame(first,-1),b=Frame(next,first);int weight=(int)Math.Round((p-first)*256),inverse=256-weight;
            if(weight==0||first==next){Array.Copy(a,output,output.Length);return;}
            for(int i=0;i<output.Length;i++){
                int value=0;for(int shift=0;shift<32;shift+=8)value|=((((a[i]>>shift)&255)*inverse+((b[i]>>shift)&255)*weight+128)>>8)<<shift;
                output[i]=value;
            }
        }
        public void Dispose(){file.Dispose();}
    }
}
