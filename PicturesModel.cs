using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Media.Animation;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Reflection.Metadata;

namespace ImgView
{
    public class PicturesModel
    {
        static readonly string[] _pictureExtensions = new string[] { ".PNG", ".JPEG", ".JPG", ".BMP", ".WEBP"};

        List<FileInfo> _files;
        int _index;
        int _index2;
        /// <summary>
        /// 表紙画像の取得
        /// </summary>
        /// <param name="file">ファイルパス</param>
        /// <returns>表紙画像のBitmapSource</returns>
        public static BitmapSource GetCoverPage(string file)
        {
            var ext = Path.GetExtension(file).ToUpper();
            if (_pictureExtensions.Contains(ext) == true)
            {
                // 画像ファイル
                var Location = Path.GetDirectoryName(file);
                var filename = Path.GetFileName(file);
                return LoadImage(new FileInfo{
                    FileName = filename,
                    Location = Location,
                    LocationType = "Dir",
                });
            }
            if (ext == ".ZIP" || ext == ".EPUB")
            {
                // ZIPファイル
                var Location = file;
                using (var zip = System.IO.Compression.ZipFile.OpenRead(file))
                {
                    var e = zip.Entries
                        .Where(x => _pictureExtensions.Contains(Path.GetExtension(x.FullName).ToUpper()))
                        .First();
                    
                    if (e != null)
                    {
                        return LoadImage(new FileInfo{
                            FileName = e.FullName,
                            Location = Location,
                            LocationType = "Zip",
                        });
                    }                    
                }
            }
            return null;
        }
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="files">PlayListのファイルの一覧</param>
        /// <param name="index">PlayListの選択位置、開始位置</param>
        public PicturesModel(string[] files, int index = -1)
        {
            _files = new List<FileInfo>();


            int playListIndex = 0;  // PlayListのインデックス
            int imagesIndex = 0;  // 画像のインデックス
            _index = -1;
            _index2 = -1;
            foreach(var file in files)
            {
                var ext = Path.GetExtension(file).ToUpper();
                if (_pictureExtensions.Contains(ext) == true)
                {
                    // 画像ファイル
                    var Location = Path.GetDirectoryName(file);
                    var filename = Path.GetFileName(file);
                    _files.Add(new FileInfo{
                        FileName = filename,
                        Location = Location,
                        LocationType = "Dir",
                    });
                    if (playListIndex==index)
                    {
                        _index = imagesIndex;
                    }
                    playListIndex++;
                    imagesIndex++;
                    continue;
                }
                if (ext == ".ZIP")
                {
                    // ZIPファイル
                    var Location = file;
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(file))
                    {
                        var entries = zip.Entries
                            .Where(x => _pictureExtensions.Contains(Path.GetExtension(x.FullName).ToUpper()) );

                        if (entries.Any() == true)                        
                        {
                            if (playListIndex==index)
                            {
                                _index = imagesIndex;
                            }
                            playListIndex++;
                            foreach(var e in entries)
                            {
                                var filename = e.FullName;
                                _files.Add(new FileInfo{
                                    FileName = filename,
                                    Location = Location,
                                    LocationType = "Zip",
                                });
                                imagesIndex++;
                            }
                        }
                    }
                    continue;
                }
            }
        }

        static readonly int BitmapSourceCacheMax = 1000;
        static public Dictionary<string, BitmapSource> BitmapSourceCacheDictionay = new();

        static public BitmapSource LoadCacheImage(FileInfo info)
        {
            // キャッシュにあるか
            var cacheKey = Path.Combine(info.Location, info.FileName);
            if (BitmapSourceCacheDictionay.ContainsKey(cacheKey))
            {
                return BitmapSourceCacheDictionay[cacheKey];
            }

            // キャッシュ無し
            BitmapImage bi = new BitmapImage();
            // ストリームを開く
            if (info.LocationType == "Zip")
            {
                // ZIP
                using (var zip = System.IO.Compression.ZipFile.OpenRead(info.Location))
                {
                    using(var fs = zip.GetEntry(info.FileName).Open())
                    {
                        using (var ms = new MemoryStream())
                        {


                            fs.CopyTo(ms);
                            ms.Seek(0, SeekOrigin.Begin);
                            

                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.StreamSource = ms;
                            bi.EndInit();
                            ms.SetLength(0);
                        }                        
                    }
                }
            }
            else
            {
                // Direcotry
                var path = Path.Join(info.Location, info.FileName);
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        ms.Seek(0, SeekOrigin.Begin);

                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        ms.SetLength(0);
                    }
                }
            }
            bi.Freeze();

            var bs = ConvertToBgra32(bi);

            // キャッシュ数の最大値を超えている場合
            if (BitmapSourceCacheDictionay.Count >= BitmapSourceCacheMax)
            {
                var removeCacheKey = BitmapSourceCacheDictionay.First().Key;
                lock(BitmapSourceCacheDictionay)
                {
                    if (BitmapSourceCacheDictionay.ContainsKey(removeCacheKey))
                    {
                        BitmapSourceCacheDictionay.Remove(removeCacheKey);
                    }
                }
            }

            // キャッシュに追加
            lock(BitmapSourceCacheDictionay)
            {
                if (BitmapSourceCacheDictionay.ContainsKey(cacheKey) == false)
                {
                    BitmapSourceCacheDictionay[cacheKey] = bs;
                }
            }

            return BitmapSourceCacheDictionay[cacheKey];
        }
        
        // キャッシュに先読み
        static public void LoadAheadImage(string path)
        {
            Debug.Print("LoadAheadImage開始");
            
            var ext = Path.GetExtension(path).ToUpper();
            if (ext == ".ZIP")
            {
                // ZIPファイル
                var Location = path;
                using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
                {
                    var es = zip.Entries
                        .Where(x => _pictureExtensions.Contains(Path.GetExtension(x.FullName).ToUpper()));
                    
                    foreach(var e in es)
                    {
                        LoadCacheImage(new FileInfo{
                            FileName = e.FullName,
                            Location = Location,
                            LocationType = "Zip",
                        });
                    }
                }
            }
            if (_pictureExtensions.Contains(ext))
            {
                LoadCacheImage(new FileInfo{
                    FileName = Path.GetFileName(path),
                    Location = Path.GetDirectoryName(path),
                    LocationType = "Dir",
                });
            }
            /*

            var dir = path;
            if (_pictureExtensions.Contains(ext) == true)
            {
                // 画像ファイル
                dir = Path.GetDirectoryName(path);
            }

            if (Directory.Exists(dir) == false) return;

            var ess = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .Where(x => _pictureExtensions.Contains(Path.GetExtension(x).ToUpper()));
            foreach(var e in ess)
            {
                LoadCacheImage(new FileInfo{
                    FileName = Path.GetFileName(e),
                    Location = dir,
                    LocationType = "Dir",
                });
            }
            */
            Debug.Print("LoadAheadImage終了");
            return;
        }

        static private BitmapSource LoadImage(FileInfo info)
        {
            return LoadCacheImage(info);

            /*
            var bi = new BitmapImage();

            // ストリームを開く
            if (info.LocationType == "Zip")
            {
                // ZIP
                using (var zip = System.IO.Compression.ZipFile.OpenRead(info.Location))
                {
                    using(var fs = zip.GetEntry(info.FileName).Open())
                    {
                        using (var ms = new MemoryStream())
                        {


                            fs.CopyTo(ms);
                            ms.Seek(0, SeekOrigin.Begin);
                            

                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.StreamSource = ms;
                            bi.EndInit();
                            ms.SetLength(0);
                        }                        
                    }
                }
            }
            else
            {
                // Direcotry
                var path = Path.Join(info.Location, info.FileName);
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        ms.Seek(0, SeekOrigin.Begin);

                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        ms.SetLength(0);
                    }
                }
            }
            bi.Freeze();

            return ConvertToBgra32(bi);
            */
        }
        static private BitmapSource ConvertToBgra32(BitmapSource bi)
        {

            if (bi.Format != PixelFormats.Bgra32)
            {
                bi = new FormatConvertedBitmap(
                    bi,
                    PixelFormats.Bgra32,
                    null,
                    0);
                bi.Freeze();
            }

            return ConvertToDPI96(bi);
        }
        static private BitmapSource ConvertToDPI96(BitmapSource bi)
        {
            const double dpi = 96;
            if (bi.DpiX != dpi || bi.DpiY != dpi)
            {
                int width = bi.PixelWidth;
                int height = bi.PixelHeight;
                int stride = width * 4;
                byte[] pixelData = new byte[stride * height];
                bi.CopyPixels(pixelData, stride, 0);

                bi = BitmapSource.Create(
                    width,
                    height,
                    dpi,
                    dpi,
                    PixelFormats.Bgra32,
                    null,
                    pixelData,
                    stride);
                bi.Freeze();
            }
            return bi;
        }
        static private BitmapSource PlaceOnCanvasImage(BitmapSource bi, BitmapSource bi2=null)
        {
            int stride = bi.PixelWidth * 4;
            byte[] datas = new byte[stride * bi.PixelHeight];

            bi.CopyPixels(new Int32Rect(0, 0, bi.PixelWidth, bi.PixelHeight), datas, stride, 0);

            WriteableBitmap w;

            if (bi2==null)
            {
                // 画像１枚
                int width = (int)((bi.PixelHeight / 9.0) * 16.0);
                if (width < bi.PixelWidth) width = bi.PixelWidth;

                w  = new WriteableBitmap(width, bi.PixelHeight, bi.DpiX, bi.DpiY, PixelFormats.Bgra32, null);
                int x = (int)((width - bi.PixelWidth)/2);
                if (x < 0)
                {
                    x = 0;
                }
                w.WritePixels(
                    new Int32Rect(x, 0, bi.PixelWidth, bi.PixelHeight),
                    datas,
                    stride,
                    0);
            }
            else
            {
                // 画像２枚
                int stride2 = bi2.PixelWidth * 4;
                byte[] datas2 = new byte[stride2 * bi2.PixelHeight];

                int height = (bi2.PixelHeight>bi.PixelHeight) ? bi2.PixelHeight : bi.PixelHeight;
                //int width = (int)((height / 9.0) * 16.0);
                int width = bi2.PixelWidth + bi.PixelWidth;
                //if (width < (bi.PixelWidth+bi2.PixelWidth)) width = bi.PixelWidth+bi2.PixelWidth;

                w  = new WriteableBitmap(width, height, bi.DpiX, bi.DpiY, PixelFormats.Bgra32, null);

                bi2.CopyPixels(new Int32Rect(0, 0, bi2.PixelWidth, bi2.PixelHeight), datas2, stride2, 0);

                /*
                int x = (int)((width - (bi.PixelWidth+bi2.PixelWidth))/2);
                if (x < 0)
                {
                    x = 0;
                }
                */
                int x = 0;
                w.WritePixels(
                    new Int32Rect(x, 0, bi2.PixelWidth, bi2.PixelHeight),
                    datas2,
                    stride2,
                    0);
                w.WritePixels(
                    new Int32Rect(x+bi2.PixelWidth, 0, bi.PixelWidth, bi.PixelHeight),
                    datas,
                    stride,
                    0);
            }

            w.Freeze();
            return w;
        }

        public string CurrentImageName{ get; private set; } = "";
        public BitmapSource CurrentImage
        {
            get
            {
                if (_index == -1) return null;

                if (_index == 0 || _index == (_files.Count-1) || _files.Count == 1 || _files[_index].Location != _files[_index+1].Location || _files[_index].Location != _files[_index-1].Location)
                {
                    CurrentImageName = _files[_index].FileName + " ";
                    _index2 = -1;

                    return PlaceOnCanvasImage(LoadImage(_files[_index]));
                }
                else
                {
                    var ri = LoadImage(_files[_index]);
                    if (ri.PixelWidth > ri.PixelHeight)
                    {
                        _index2 = -1;
                        // 横長
                        return PlaceOnCanvasImage(ri);
                    }

                    _index2 = _index + 1;
                    CurrentImageName = _files[_index2].FileName + "|" + _files[_index].FileName + " ";

                    return PlaceOnCanvasImage(ri, LoadImage(_files[_index2]));
                }
            }
        }
        public bool MoveNext()
        {
            if (_index == -1) return false;
            if (_files.Count == (_index+1)) return false;
            if (_files.Count == (_index2+1)) return false;

            if (_index2 != -1)
            {
                _index = _index2+1;
                _index2 = -1;
            }
            else
            {
                _index++;
            }



            return true;
        }
        public bool MovePrevious()
        {
            BitmapSource bs;

            if (_index == -1) return false; // 画像なし
            if (_index == 0) return false;  // 先頭のためこれ以上戻れない
            
            int fileCounter = 0;
            if (_files[_index].Location != _files[_index-1].Location)
            {
                //return true;  // ロケーション(Zip or Dir)の先頭
                var i = _index-1;
                var location = _files[i].Location;
                while(i >= 0 && _files[i].Location == location)
                {
                    fileCounter++;
                    i--;
                }
            }

            _index2 = -1;
            _index--;   // 1ページ戻る

            if (fileCounter > 0 && fileCounter%2 == 0)
            {
                // 偶数
                return true;
            }

            if (_index == 0) return true;  // 先頭のためこれ以上戻れない

            bs = LoadImage(_files[_index]);
            if (bs.PixelWidth > bs.PixelHeight) return true;    // 横長

            if (_files[_index].Location != _files[_index-1].Location) return true;  // ロケーション(Zip or Dir)の先頭

            _index2 = _index;
            _index--;   // 2ページ戻る

            return true;
        }
    }
}