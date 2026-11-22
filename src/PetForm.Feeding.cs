using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace XiaoJingDesktopPet {
 public sealed partial class PetForm {
  private sealed class FoodContact {
   internal Rectangle Bounds;internal float BellyX;
   internal float BiteTop, BiteBottom;
   internal float[] BiteCentres;
  }
  private readonly Dictionary<Image,FoodContact> foodContacts=new Dictionary<Image,FoodContact>();
  private readonly Dictionary<Image,Bitmap> feedingFoodRasters=new Dictionary<Image,Bitmap>();
  private const int FoodLayerSize=56;
  private readonly int[] foodLayerPixels=new int[FoodLayerSize*FoodLayerSize];
  private Bitmap foodLayer;
  private Bitmap feedingFish;
  private Bitmap feedingComposite;
  private Image FeedingFoodImage(){
   Image image;
   if(TryCurrentFoodImage(out image)||images.TryGetValue("life/fish-2",out image))return image;
   if(feedingFish==null){
    feedingFish=new Bitmap(160,128,PixelFormat.Format32bppPArgb);
    using(var g=Graphics.FromImage(feedingFish)){
     g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(4,4);g.TranslateTransform(5,16);
     using(var body=new SolidBrush(Color.FromArgb(245,164,91)))using(var highlight=new SolidBrush(Color.FromArgb(255,215,155)))using(var line=new Pen(Color.FromArgb(124,76,63),1)){
      g.FillPolygon(body,new[]{new PointF(16,0),new PointF(27,-7),new PointF(27,7)});g.FillEllipse(body,0,-8,21,16);g.DrawEllipse(line,0,-8,21,16);g.FillEllipse(highlight,4,1,11,4);g.FillEllipse(Brushes.DarkSlateGray,4,-3,2.5F,2.5F);
     }
    }
   }return feedingFish;
  }
  private FoodContact FoodSupport(Image image){
   FoodContact contact;if(foodContacts.TryGetValue(image,out contact))return contact;
   int[] p=ScenePixels.Read(image);int x0=image.Width,y0=image.Height,x1=-1,y1=-1;
   for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(((p[y*image.Width+x]>>24)&255)>160){x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
   if(x1<0)throw new InvalidOperationException("Food image has no visible content.");
   var belly=new List<int>();for(int y=Math.Max(y0,y1-1);y<=y1;y++)for(int x=x0;x<=x1;x++)if(((p[y*image.Width+x]>>24)&255)>160)belly.Add(x);
   belly.Sort();contact=new FoodContact{Bounds=Rectangle.FromLTRB(x0,y0,x1+1,y1+1),BellyX=belly[belly.Count/2]-x0};foodContacts.Add(image,contact);return contact;
  }
  private PointF FeedingSupportPoint(long elapsed){
   LeisureProfile profile=GeneralToolProfile();if(profile==null||profile.feedHand==null)return new PointF(120,116);
   var palm=new PointF(profile.feedHand[0],profile.feedHand[1]);var mouth=new PointF(profile.feedMouth[0],profile.feedMouth[1]+10);
   return EnsureCharacterPlayer()?characterPlayer.FeedingPoint(palm,mouth,elapsed):palm;
  }
  private Image PreparedFeedingFoodImage(Image source){
   Bitmap raster;if(feedingFoodRasters.TryGetValue(source,out raster))return raster;
   FoodContact original=FoodSupport(source);float fit=Math.Min(27F/original.Bounds.Width,21F/original.Bounds.Height);
   raster=new Bitmap(Math.Max(1,(int)Math.Round(original.Bounds.Width*fit)),Math.Max(1,(int)Math.Round(original.Bounds.Height*fit)),PixelFormat.Format32bppPArgb);
   using(var g=Graphics.FromImage(raster)){
    g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
    g.DrawImage(source,new Rectangle(0,0,raster.Width,raster.Height),original.Bounds,GraphicsUnit.Pixel);
   }
   // Calibrate the visible belly after rasterization. Thin food tips can
   // otherwise move several pixels when the large source is fitted to 30px.
   feedingFoodRasters.Add(source,raster);return raster;
  }
  private PointF FeedingLips(){
   LeisureProfile profile=GeneralToolProfile();
   if(profile==null||profile.feedMouth==null)return new PointF(126,89);
   return new PointF(profile.feedMouth[0],profile.feedMouth[1]);
  }

  private void DrawFeedingFoodOnly(Graphics g,long elapsed){
   DrawFoodLayer(g,elapsed,null);
  }

  private void DrawFoodLayer(Graphics g,long elapsed,CharacterActionPlayer actor){
   if(elapsed<0||elapsed>=FeedingCycle.FoodEnd)return;
   Image source=FeedingFoodImage(),fishArt;
   Image food=PreparedFeedingFoodImage(source);FoodContact contact=FoodSupport(food);
   PointF hand=FeedingSupportPoint(elapsed), lips=FeedingLips();
   bool fish=Object.ReferenceEquals(source,feedingFish)||
            (images.TryGetValue("life/fish-2",out fishArt)&&Object.ReferenceEquals(source,fishArt));
   float finalTilt=fish?-26F:-9F;
   if(contact.BiteCentres==null)MeasureBiteEdge(food,contact,finalTilt);
   double flight=FeedingCycle.Smooth(elapsed/650.0);
   double carry=FeedingCycle.CarryProgress(elapsed);
   double approach=FeedingCycle.Smooth((carry-.22)/.78);
   double first=FeedingCycle.Smooth((elapsed-FeedingCycle.FirstBite)/180.0);
   double last=FeedingCycle.Smooth((elapsed-FeedingCycle.SecondBite)/205.0);
   double eaten=.36*first+.64*last;
   float width=contact.Bounds.Width,height=contact.Bounds.Height;
   float tilt=(float)((1-flight)*-10+approach*finalTilt);
   // The underside lands on the open palm. A toss from the receiving side
   // leaves the face clear even for the wide winter hoods.
   float cx=hand.X+width/2-contact.BellyX,cy=hand.Y-height/2+1;
   if(elapsed<650){
    cx=(float)(18+(cx-18)*flight);
    cy=(float)(cy+17*(1-flight)-5*Math.Sin(Math.PI*flight));
   }else{
    double row=eaten*(contact.BiteCentres.Length-1);
    int i=(int)row,j=Math.Min(i+1,contact.BiteCentres.Length-1);
    double biteX=contact.BiteCentres[i]+(contact.BiteCentres[j]-contact.BiteCentres[i])*(row-i);
    double biteY=contact.BiteTop+(contact.BiteBottom-contact.BiteTop+3)*eaten;
    cx+=(float)((lips.X-biteX-cx)*approach);
    cy+=(float)((lips.Y+1-biteY-cy)*approach);
   }
   float x=cx-width/2,y=cy-height/2;
   int left=(int)Math.Floor(x)-10,top=(int)Math.Floor(y)-10;
   if(foodLayer==null)foodLayer=new Bitmap(FoodLayerSize,FoodLayerSize,PixelFormat.Format32bppPArgb);
   using(var paint=Graphics.FromImage(foodLayer)){
    paint.Clear(Color.Transparent);paint.InterpolationMode=InterpolationMode.HighQualityBicubic;paint.PixelOffsetMode=PixelOffsetMode.HighQuality;
    paint.TranslateTransform(x-left+width/2,y-top+height/2);paint.RotateTransform(tilt);
    paint.DrawImage(food,new RectangleF(-width/2,-height/2,width,height),contact.Bounds,GraphicsUnit.Pixel);
   }
   var rect=new Rectangle(0,0,FoodLayerSize,FoodLayerSize);
   var data=foodLayer.LockBits(rect,ImageLockMode.ReadWrite,PixelFormat.Format32bppPArgb);
   try{
    Marshal.Copy(data.Scan0,foodLayerPixels,0,foodLayerPixels.Length);
    for(int row=0;row<FoodLayerSize;row++)for(int column=0;column<FoodLayerSize;column++){
     int i=row*FoodLayerSize+column,v=foodLayerPixels[i];if((v>>24)==0)continue;
     int px=left+column,py=top+row;
     double alpha=1;
     if(eaten>0){
      // The leading edge enters at the lips. Only the part crossing that
      // edge disappears, and the remaining piece keeps its original scale.
      double lipLine=lips.Y+.3+Math.Min(2,Math.Abs(px-lips.X)*.10);
      alpha=FeedingCycle.Smooth((py-lipLine+.55)/1.2);
     }
     if(actor!=null && elapsed>=470)alpha*=1-actor.FeedingHandCover(px,py,hand,lips,elapsed);
     if(alpha<.999)foodLayerPixels[i]=ScaleFoodAlpha(v,alpha);
    }
    Marshal.Copy(foodLayerPixels,0,data.Scan0,foodLayerPixels.Length);
   }finally{foodLayer.UnlockBits(data);}
   g.DrawImageUnscaled(foodLayer,left+8,top+122);
  }

  private static void MeasureBiteEdge(Image food,FoodContact contact,float tilt){
   // Trace the cross sections of the rotated illustration once. A round bun
   // and a fish tail then pass through the same mouth at different offsets.
   const int bins=48;
   var sum=new double[bins];var weights=new double[bins];
   double angle=tilt*Math.PI/180,cos=Math.Cos(angle),sin=Math.Sin(angle);
   int[] ink=ScenePixels.Read(food);var box=contact.Bounds;
   float top=float.MaxValue,bottom=float.MinValue;
   for(int y=0;y<box.Height;y++)for(int x=0;x<box.Width;x++){
    int alpha=(int)((uint)ink[(box.Y+y)*food.Width+box.X+x]>>24);
    if(alpha<96)continue;
    double dx=x+.5-box.Width*.5,dy=y+.5-box.Height*.5;
    float ry=(float)(sin*dx+cos*dy);top=Math.Min(top,ry);bottom=Math.Max(bottom,ry);
    int bin=Math.Max(0,Math.Min(bins-1,(int)Math.Round(ry)+bins/2));
    sum[bin]+=(cos*dx-sin*dy)*alpha;weights[bin]+=alpha;
   }
   int lo=Math.Max(0,(int)Math.Round(top)+bins/2),hi=Math.Min(bins-1,(int)Math.Round(bottom)+bins/2);
   contact.BiteTop=top-.5F;contact.BiteBottom=bottom+.5F;contact.BiteCentres=new float[hi-lo+1];
   for(int i=lo;i<=hi;i++){
    double w=0,value=0;
    for(int j=Math.Max(lo,i-2);j<=Math.Min(hi,i+2);j++){
     int kernel=3-Math.Abs(i-j);w+=kernel*weights[j];value+=kernel*sum[j];
    }
    contact.BiteCentres[i-lo]=w>0?(float)(value/w):0;
   }
  }

  private void DrawFeedingActor(Graphics screen,Image actor,long elapsed){
   if(feedingComposite==null)feedingComposite=new Bitmap(256,208,PixelFormat.Format32bppPArgb);
   using(var local=Graphics.FromImage(feedingComposite)){
    local.Clear(Color.Transparent);
    local.DrawImage(actor,new Rectangle(0,0,256,208),0,0,actor.Width,actor.Height,GraphicsUnit.Pixel);
    local.TranslateTransform(-8,-122);
    DrawFoodLayer(local,elapsed,characterPlayer);
   }
   screen.DrawImage(feedingComposite,new RectangleF(8,122,256,208),new RectangleF(0,0,256,208),GraphicsUnit.Pixel);
  }

  private static int ScaleFoodAlpha(int pixel,double opacity){
   int weight=(int)Math.Round(256*opacity),result=0;
   for(int shift=0;shift<=24;shift+=8)result|=((((pixel>>shift)&255)*weight+128)>>8)<<shift;
   return result;
  }

  private void DrawFeeding(Graphics g,long elapsed){
   if(elapsed>4300){
    float rise=(elapsed-4300)/35F;int alpha=Clamp((int)(255*(5400-elapsed)/700.0),0,255);
    using(var brush=new SolidBrush(AlphaColor(alpha,255,149,166)))using(var font=UiFont(16F,FontStyle.Bold))g.DrawString("♥",font,brush,182,171-rise);
   }
  }
  private void ReleaseFeedingFood(){if(feedingComposite!=null){feedingComposite.Dispose();feedingComposite=null;}foreach(var image in feedingFoodRasters.Values)image.Dispose();feedingFoodRasters.Clear();if(feedingFish!=null){feedingFish.Dispose();feedingFish=null;}if(foodLayer!=null){foodLayer.Dispose();foodLayer=null;}foodContacts.Clear();}
 }
}
