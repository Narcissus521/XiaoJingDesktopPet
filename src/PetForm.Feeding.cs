using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
namespace XiaoJingDesktopPet {
 public sealed partial class PetForm {
  private sealed class FoodContact {
   internal Rectangle Bounds;internal float BellyX;
  }
  private readonly Dictionary<Image,FoodContact> foodContacts=new Dictionary<Image,FoodContact>();
  private readonly Dictionary<Image,Bitmap> feedingFoodRasters=new Dictionary<Image,Bitmap>();
  private Bitmap feedingFish;
  private Image FeedingFoodImage(){
   Image image;if(TryCurrentFoodImage(out image))return image;
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
  private Image PreparedFeedingFoodImage(){
   Image source=FeedingFoodImage();Bitmap raster;if(feedingFoodRasters.TryGetValue(source,out raster))return raster;
   FoodContact original=FoodSupport(source);float fit=Math.Min(30F/original.Bounds.Width,24F/original.Bounds.Height);
   raster=new Bitmap(Math.Max(1,(int)Math.Round(original.Bounds.Width*fit)),Math.Max(1,(int)Math.Round(original.Bounds.Height*fit)),PixelFormat.Format32bppPArgb);
   using(var g=Graphics.FromImage(raster)){
    g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
    g.DrawImage(source,new Rectangle(0,0,raster.Width,raster.Height),original.Bounds,GraphicsUnit.Pixel);
   }
   // Calibrate the visible belly after rasterization. Thin food tips can
   // otherwise move several pixels when the large source is fitted to 30px.
   feedingFoodRasters.Add(source,raster);return raster;
  }
  private void FoodPlacement(long elapsed,out Image food,out FoodContact contact,out PointF support,out float scale){
   food=PreparedFeedingFoodImage();contact=FoodSupport(food);PointF at=FeedingSupportPoint(elapsed);
   float p=(float)FeedingCycle.Smooth(Math.Min(1,elapsed/650.0));
   support=elapsed<650?new PointF(234+(at.X+8-234)*p,238+(at.Y+122-238)*p-(float)Math.Sin(p*Math.PI)*38):new PointF(at.X+8,at.Y+122);
   scale=FeedingCycle.FoodScale(elapsed)/.85F;
   // The receiving palm supports the belly; at the mouth the fingers hold the
   // sides. Keep the food's upper edge at the lips, below the cheeks.
   support.Y+=Math.Max(0,contact.Bounds.Height-12)*(float)FeedingCycle.CarryProgress(elapsed);
  }
  private void DrawFeedingFoodOnly(Graphics g,long elapsed){
   if(elapsed<0||elapsed>=FeedingCycle.FoodEnd)return;
   Image food;FoodContact contact;PointF at;float scale;FoodPlacement(elapsed,out food,out contact,out at,out scale);
   var rectangle=new RectangleF(at.X-contact.BellyX*scale,at.Y-(contact.Bounds.Height-1)*scale,contact.Bounds.Width*scale,contact.Bounds.Height*scale);
   g.DrawImage(food,rectangle,contact.Bounds,GraphicsUnit.Pixel);
  }
  private void DrawFeeding(Graphics g,long elapsed){
   DrawFeedingFoodOnly(g,elapsed);
   if(elapsed>=0&&elapsed<FeedingCycle.FoodEnd&&EnsureCharacterPlayer()){
    Image food;FoodContact contact;PointF at;float scale;FoodPlacement(elapsed,out food,out contact,out at,out scale);
    characterPlayer.DrawFeedingFingers(g,FeedingSupportPoint(elapsed),contact.Bounds.Width*scale,contact.Bounds.Height*scale,elapsed);
   }
   if(elapsed>4300){
    float rise=(elapsed-4300)/35F;int alpha=Clamp((int)(255*(5400-elapsed)/700.0),0,255);
    using(var brush=new SolidBrush(AlphaColor(alpha,255,149,166)))using(var font=UiFont(16F,FontStyle.Bold))g.DrawString("♥",font,brush,182,171-rise);
   }
  }
  private void ReleaseFeedingFood(){foreach(var image in feedingFoodRasters.Values)image.Dispose();feedingFoodRasters.Clear();if(feedingFish!=null){feedingFish.Dispose();feedingFish=null;}foodContacts.Clear();}
 }
}
