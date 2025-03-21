using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ClosedXML.Utils;
using NUnit.Framework;
using System;
using SixLabors.ImageSharp;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ClosedXML.Tests
{
    [TestFixture]
    public class PictureTests
    {
        [Test]
        public void CanAddPictureFromStream()
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1");

                using (var resourceStream = Assembly.GetAssembly(typeof(ClosedXML.Examples.BasicTable)).GetManifestResourceStream("ClosedXML.Examples.Resources.SampleImage.jpg"))
                {
                    var picture = ws.AddPicture(resourceStream, "MyPicture")
                        .WithPlacement(XLPicturePlacement.FreeFloating)
                        .MoveTo(50, 50)
                        .WithSize(200, 200);

                    Assert.AreEqual(XLPictureFormat.Jpeg, picture.Format);
                    Assert.AreEqual(200, picture.Width);
                    Assert.AreEqual(200, picture.Height);
                }
            }
        }

        [Test]
        public void CanAddPictureFromFile()
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1");

                var path = Path.ChangeExtension(Path.GetTempFileName(), "jpg");

                try
                {
                    using (var resourceStream = Assembly.GetAssembly(typeof(ClosedXML.Examples.BasicTable)).GetManifestResourceStream("ClosedXML.Examples.Resources.SampleImage.jpg"))
                    using (var fileStream = File.Create(path))
                    {
                        resourceStream.Seek(0, SeekOrigin.Begin);
                        resourceStream.CopyTo(fileStream);
                        fileStream.Close();
                    }

                    var picture = ws.AddPicture(path)
                        .WithPlacement(XLPicturePlacement.FreeFloating)
                        .MoveTo(50, 50);

                    Assert.AreEqual(XLPictureFormat.Jpeg, picture.Format);
                    Assert.AreEqual(400, picture.Width);
                    Assert.AreEqual(400, picture.Height);
                }
                finally
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
        }

        [Test]
        public void CanAddPictureConcurrentlyFromFile()
        {
            var path = Path.ChangeExtension(Path.GetTempFileName(), "jpg");

            try
            {
                using (var resourceStream = Assembly.GetAssembly(typeof(ClosedXML.Examples.BasicTable)).GetManifestResourceStream("ClosedXML.Examples.Resources.SampleImage.jpg"))
                using (var fileStream = File.Create(path))
                {
                    resourceStream.Seek(0, SeekOrigin.Begin);
                    resourceStream.CopyTo(fileStream);
                    fileStream.Close();
                }

                Parallel.Invoke(() => verifyAddImageFromFile(path), () => verifyAddImageFromFile(path));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private void verifyAddImageFromFile(string filePath)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1");

                var picture = ws.AddPicture(filePath)
                           .WithPlacement(XLPicturePlacement.FreeFloating)
                           .MoveTo(50, 50);

                Assert.AreEqual(XLPictureFormat.Jpeg, picture.Format);
                Assert.AreEqual(400, picture.Width);
                Assert.AreEqual(50, picture.Top);
            }
        }

        [Test]
        public void CanScaleImage()
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1");

                using (var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
                {
                    var pic = ws.AddPicture(resourceStream, "MyPicture")
                        .WithPlacement(XLPicturePlacement.FreeFloating)
                        .MoveTo(50, 50);

                    Assert.AreEqual(252, pic.OriginalWidth);
                    Assert.AreEqual(152, pic.OriginalHeight);
                    Assert.AreEqual(252, pic.Width);
                    Assert.AreEqual(152, pic.Height);

                    pic.ScaleHeight(0.7);
                    pic.ScaleWidth(1.2);

                    Assert.AreEqual(252, pic.OriginalWidth);
                    Assert.AreEqual(152, pic.OriginalHeight);
                    Assert.AreEqual(302, pic.Width);
                    Assert.AreEqual(106, pic.Height);

                    pic.ScaleHeight(0.7);
                    pic.ScaleWidth(1.2);

                    Assert.AreEqual(252, pic.OriginalWidth);
                    Assert.AreEqual(152, pic.OriginalHeight);
                    Assert.AreEqual(362, pic.Width);
                    Assert.AreEqual(74, pic.Height);

                    pic.ScaleHeight(0.8, true);
                    pic.ScaleWidth(1.1, true);

                    Assert.AreEqual(252, pic.OriginalWidth);
                    Assert.AreEqual(152, pic.OriginalHeight);
                    Assert.AreEqual(277, pic.Width);
                    Assert.AreEqual(122, pic.Height);
                }
            }
        }

        [Test]
        public void TestDefaultPictureNames()
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1");

                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
                {
                    ws.AddPicture(stream, XLPictureFormat.Png);
                    stream.Position = 0;

                    ws.AddPicture(stream, XLPictureFormat.Png);
                    stream.Position = 0;

                    ws.AddPicture(stream, XLPictureFormat.Png).Name = "Picture 4";
                    stream.Position = 0;

                    ws.AddPicture(stream, XLPictureFormat.Png);
                    stream.Position = 0;
                }

                Assert.AreEqual("Picture 1", ws.Pictures.Skip(0).First().Name);
                Assert.AreEqual("Picture 2", ws.Pictures.Skip(1).First().Name);
                Assert.AreEqual("Picture 4", ws.Pictures.Skip(2).First().Name);
                Assert.AreEqual("Picture 5", ws.Pictures.Skip(3).First().Name);
            }
        }

        [Test]
        public void TestDefaultIds()
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1");

                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
                {
                    ws.AddPicture(stream, XLPictureFormat.Png);
                    stream.Position = 0;

                    ws.AddPicture(stream, XLPictureFormat.Png);
                    stream.Position = 0;

                    ws.AddPicture(stream, XLPictureFormat.Png).Name = "Picture 4";
                    stream.Position = 0;

                    ws.AddPicture(stream, XLPictureFormat.Png);
                    stream.Position = 0;
                }

                Assert.AreEqual(1, ws.Pictures.Skip(0).First().Id);
                Assert.AreEqual(2, ws.Pictures.Skip(1).First().Id);
                Assert.AreEqual(3, ws.Pictures.Skip(2).First().Id);
                Assert.AreEqual(4, ws.Pictures.Skip(3).First().Id);
            }
        }

        [Test]
        public void XLMarkerTests()
        {
            IXLWorksheet ws = new XLWorkbook().Worksheets.Add("Sheet1");
            XLMarker firstMarker = new XLMarker(ws.Cell(1, 10), new Point(100, 0));

            Assert.AreEqual(10, firstMarker.ColumnNumber);
            Assert.AreEqual(1, firstMarker.RowNumber);
            Assert.AreEqual(100, firstMarker.Offset.X);
            Assert.AreEqual(0, firstMarker.Offset.Y);
        }

        [Test]
        public void XLPictureTests()
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");

                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
                {
                    var pic = ws.AddPicture(stream, XLPictureFormat.Png, "Image1")
                        .WithPlacement(XLPicturePlacement.FreeFloating)
                        .MoveTo(220, 155);

                    Assert.AreEqual(XLPicturePlacement.FreeFloating, pic.Placement);
                    Assert.AreEqual("Image1", pic.Name);
                    Assert.AreEqual(XLPictureFormat.Png, pic.Format);
                    Assert.AreEqual(252, pic.OriginalWidth);
                    Assert.AreEqual(152, pic.OriginalHeight);
                    Assert.AreEqual(252, pic.Width);
                    Assert.AreEqual(152, pic.Height);
                    Assert.AreEqual(220, pic.Left);
                    Assert.AreEqual(155, pic.Top);
                }
            }
        }

        [Test]
        public void CanLoadFileWithImagesAndCopyImagesToNewSheet()
        {
            using (var stream = TestHelper.GetStreamFromResource(TestHelper.GetResourcePath(@"Examples\ImageHandling\ImageAnchors.xlsx")))
            using (var wb = new XLWorkbook(stream))
            {
                var ws = wb.Worksheets.First();
                Assert.AreEqual(2, ws.Pictures.Count);

                var copy = ws.CopyTo("NewSheet");
                Assert.AreEqual(2, copy.Pictures.Count);
            }
        }

        [Test]
        public void CanDeletePictures()
        {
            using (var ms = new MemoryStream())
            {
                int originalCount;

                using (var stream = TestHelper.GetStreamFromResource(TestHelper.GetResourcePath(@"Examples\ImageHandling\ImageAnchors.xlsx")))
                using (var wb = new XLWorkbook(stream))
                {
                    var ws = wb.Worksheets.First();
                    originalCount = ws.Pictures.Count;
                    ws.Pictures.Delete(ws.Pictures.First());

                    var pictureName = ws.Pictures.First().Name;
                    ws.Pictures.Delete(pictureName);

                    wb.SaveAs(ms);
                }

                using (var wb = new XLWorkbook(ms))
                {
                    var ws = wb.Worksheets.First();
                    Assert.AreEqual(originalCount - 2, ws.Pictures.Count);
                }
            }
        }

        [Test]
        public void PictureRenameTests()
        {
            using (var stream = TestHelper.GetStreamFromResource(TestHelper.GetResourcePath(@"Examples\ImageHandling\ImageAnchors.xlsx")))
            using (var wb = new XLWorkbook(stream))
            {
                var ws = wb.Worksheet("Images3");
                var picture = ws.Pictures.First();
                Assert.AreEqual("Picture 1", picture.Name);

                picture.Name = "picture 1";
                picture.Name = "pICture 1";
                picture.Name = "Picture 1";

                picture = ws.Pictures.Last();
                picture.Name = "new name";

                Assert.Throws<ArgumentException>(() => picture.Name = "Picture 1");
                Assert.Throws<ArgumentException>(() => picture.Name = "picTURE 1");
            }
        }

        [Test]
        public void HandleDuplicatePictureIdsAcrossWorksheets()
        {
            using (var wb = new XLWorkbook())
            {
                var ws1 = wb.AddWorksheet("Sheet1");
                var ws2 = wb.AddWorksheet("Sheet2");

                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
                {
                    (ws1 as XLWorksheet).AddPicture(stream, "Picture 1", 2);
                    (ws1 as XLWorksheet).AddPicture(stream, "Picture 2", 3);

                    //Internal method - used for loading files
                    var pic = (ws2 as XLWorksheet).AddPicture(stream, "Picture 1", 2)
                        .WithPlacement(XLPicturePlacement.FreeFloating)
                        .MoveTo(220, 155) as XLPicture;

                    var id = pic.Id;

                    pic.Id = id;
                    Assert.AreEqual(id, pic.Id);

                    pic.Id = 3;
                    Assert.AreEqual(3, pic.Id);

                    pic.Id = id;

                    var pic2 = (ws2 as XLWorksheet).AddPicture(stream, "Picture 2", 3)
                        .WithPlacement(XLPicturePlacement.FreeFloating)
                        .MoveTo(440, 300) as XLPicture;
                }
            }
        }

        [Test]
        public void CopyImageSameWorksheet()
        {
            var wb = new XLWorkbook();
            var ws1 = wb.Worksheets.Add("Sheet1");

            IXLPicture original;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
            {
                original = (ws1 as XLWorksheet).AddPicture(stream, "Picture 1", 2)
                    .WithPlacement(XLPicturePlacement.FreeFloating)
                    .MoveTo(220, 155) as XLPicture;
            }

            var copy = original.Duplicate()
                .MoveTo(300, 200) as XLPicture;

            Assert.AreEqual(2, ws1.Pictures.Count());
            Assert.AreEqual(ws1, copy.Worksheet);
            Assert.AreEqual(original.Format, copy.Format);
            Assert.AreEqual(original.Height, copy.Height);
            Assert.AreEqual(original.Placement, copy.Placement);
            Assert.AreEqual(original.TopLeftCell.ToString(), copy.TopLeftCell.ToString());
            Assert.AreEqual(original.Width, copy.Width);
            Assert.AreEqual(original.ImageStream.ToArray(), copy.ImageStream.ToArray(), "Image streams differ");

            Assert.AreEqual(200, copy.Top);
            Assert.AreEqual(300, copy.Left);
            Assert.AreNotEqual(original.Id, copy.Id);
            Assert.AreNotEqual(original.Name, copy.Name);
        }

        [Test]
        public void CopyImageDifferentWorksheets()
        {
            var wb = new XLWorkbook();
            var ws1 = wb.Worksheets.Add("Sheet1");
            IXLPicture original;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
            {
                original = (ws1 as XLWorksheet).AddPicture(stream, "Picture 1", 2)
                    .WithPlacement(XLPicturePlacement.FreeFloating)
                    .MoveTo(220, 155) as XLPicture;
            }
            var ws2 = wb.Worksheets.Add("Sheet2");

            var copy = original.CopyTo(ws2);

            Assert.AreEqual(1, ws1.Pictures.Count());
            Assert.AreEqual(1, ws2.Pictures.Count());

            Assert.AreEqual(ws2, copy.Worksheet);

            Assert.AreEqual(original.Format, copy.Format);
            Assert.AreEqual(original.Height, copy.Height);
            Assert.AreEqual(original.Left, copy.Left);
            Assert.AreEqual(original.Name, copy.Name);
            Assert.AreEqual(original.Placement, copy.Placement);
            Assert.AreEqual(original.Top, copy.Top);
            Assert.AreEqual(original.TopLeftCell.ToString(), copy.TopLeftCell.ToString());
            Assert.AreEqual(original.Width, copy.Width);
            Assert.AreEqual(original.ImageStream.ToArray(), copy.ImageStream.ToArray(), "Image streams differ");

            Assert.AreNotEqual(original.Id, copy.Id);
        }

        [Test]
        public void PictureShiftsWhenInsertingRows()
        {
            using (var wb = new XLWorkbook())
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClosedXML.Tests.Resource.Images.ImageHandling.png"))
            {
                var ws = wb.Worksheets.Add("ImageShift");
                var picture = ws.AddPicture(stream, XLPictureFormat.Png, "PngImage")
                    .MoveTo(ws.Cell(5, 2))
                    .WithPlacement(XLPicturePlacement.Move);

                ws.Row(2).InsertRowsBelow(20);

                Assert.AreEqual(25, picture.TopLeftCell.Address.RowNumber);
            }
        }

        [Test]
        public void PictureNotFound()
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1");
                Assert.Throws<ArgumentOutOfRangeException>(() => ws.Picture("dummy"));
                Assert.Throws<ArgumentOutOfRangeException>(() => ws.Pictures.Delete("dummy"));
            }
        }

        [Test]
        public void CanCopyEmfPicture()
        {
            // #1621 - There are 2 Bmp Guids: ImageFormat.Bmp and ImageFormat.MemoryBmp
            using var stream = TestHelper.GetStreamFromResource(TestHelper.GetResourcePath(@"Other\Pictures\EmfPicture.xlsx"));
            using var wb = new XLWorkbook(stream);
            var ws1 = wb.Worksheets.First();
            var img1 = ws1.Pictures.First();

            var ws2 = wb.AddWorksheet();

            var img2 = img1.CopyTo(ws2);

            Assert.AreEqual(XLPictureFormat.Emf, img2.Format);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            ms.Seek(0, SeekOrigin.Begin);

            using var wb2 = new XLWorkbook(ms);
            ws2 = wb2.Worksheet("Sheet2");
            img2 = ws2.Pictures.First();
            Assert.AreEqual(XLPictureFormat.Emf, img2.Format);
        }

        [Test]
        public void KeepOriginalDrawingShapesZOrder()
        {
            // File contains shapes and a picture in a mixed order.
            using var stream = TestHelper.GetStreamFromResource(@"Other.Pictures.ImageShapeZOrder-Input.xlsx");
            TestHelper.CreateAndCompare(
                () => new XLWorkbook(stream),
                @"Other\Pictures\ImageShapeZOrder-Output.xlsx");
        }
    }
}
