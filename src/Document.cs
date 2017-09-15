﻿using CorePDF.Contents;
using CorePDF.Embeds;
using CorePDF.Pages;
using CorePDF.TypeFaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CorePDF
{
    public class Document
    {
        public Properties Properties { get; set; } = new CorePDF.Properties();
        public Catalog Catalog { get; set; }
        public PageRoot PageRoot { get; set; }
        public decimal FontSize { get; set; } = 16m;
        public List<Font> Fonts { get; set; } = new List<Font>();
        public List<Page> Pages { get; set; } = new List<Page>();
        public List<HeaderFooter> HeadersFooters { get; set; } = new List<HeaderFooter>();
        public List<ImageFile> Images { get; set; } = new List<ImageFile>();
        public bool CompressContent { get; set; } = false;
 
        public Document()
        {
            PageRoot = new PageRoot
            {
                Document = this
            };
            Catalog = new Catalog
            {
                Document = this
            };
            Fonts.AddRange(TypeFaces.Fonts.Styles());
        }

        public void Publish(Stream baseStream)
        {
            using (StreamWriter stream = new StreamWriter(baseStream, new UTF8Encoding(false)))
            {
                stream.NewLine = "\n";

                // PDF Header
                stream.WriteLine("%PDF-{0}", "1.4"); // the version of PDF 
                stream.WriteLine("%\x82\x82\x82\x82"); // needed to allow editors to know that this is a binary file

                // place all the document objects into postion
                var objects = PositionObjects();

                // then get all the data ready for the document
                PrepareStreams();

                // Call publish on all the child objects
                Catalog.Publish(stream);

                foreach (var font in Fonts)
                {
                    font.Publish(stream);
                }

                foreach (var image in Images)
                {
                    image.Publish(stream);
                }

                PageRoot.Publish(stream);

                foreach (var headerFooter in HeadersFooters)
                {
                    headerFooter.Publish(stream);
                }

                foreach (var page in Pages)
                {
                    foreach (var content in page.Contents)
                    {
                        content.Publish(stream);
                    }
                }

                foreach (var page in Pages)
                {
                    page.Publish(Fonts, stream);
                }

                //// Document attributes
                Properties.Publish(stream);

                // PDF Cross Reference
                var startXref = stream.BaseStream.Position;
                var xref = "xref\n";
                xref += string.Format("0 {0}\n", objects + 1);
                xref += CreateXRefTable();

                stream.Write(xref);

                // PDF Trailer
                var trailer = "trailer\n";
                trailer += string.Format("<</Size {0}\n", objects + 1);
                trailer += "/Root 1 0 R\n";

                if (Properties.ObjectNumber > 0)
                {
                    trailer += string.Format("/Info {0} 0 R\n", Properties.ObjectNumber);
                }
                trailer += ">>\n";
                trailer += "startxref\n";
                trailer += startXref.ToString() + "\n";
                trailer += "%%EOF";

                // PDF END
                stream.Write(trailer);

                stream.Flush();
            }
        }

        public void PrepareStreams()
        {
            foreach (var image in Images)
            {
                image.PrepareStream();
            }

            foreach (var headerfooter in HeadersFooters)
            {
                 headerfooter.PrepareStream(Fonts, CompressContent);
            }

            foreach (var page in Pages)
            {
                page.PrepareStream(Fonts, CompressContent);
            }
        }

        /// <summary>
        /// This needs to be processed in the same order as the object position method below.
        /// </summary>
        /// <returns></returns>
        public string CreateXRefTable()
        {
            var result = "0000000000 65535 f\n";
            result += string.Format("{0} 00000 n\n", Catalog.BytePosition.ToString().PadLeft(10, '0'));

            foreach (var font in Fonts)
            {
                result += string.Format("{0} 00000 n\n", font.BytePosition.ToString().PadLeft(10, '0'));
            }

            foreach (var image in Images)
            {
                result += string.Format("{0} 00000 n\n", image.BytePosition.ToString().PadLeft(10, '0'));
            }

            result += string.Format("{0} 00000 n\n", PageRoot.BytePosition.ToString().PadLeft(10, '0'));

            foreach (var headerfooter in HeadersFooters)
            {
                foreach (var content in headerfooter.Contents)
                {
                    result += string.Format("{0} 00000 n\n", content.BytePosition.ToString().PadLeft(10, '0'));
                }
            }

            foreach (var page in Pages)
            {
                foreach (var content in page.Contents)
                {
                    result += string.Format("{0} 00000 n\n", content.BytePosition.ToString().PadLeft(10, '0'));
                }
            }

            foreach (var page in Pages)
            {
                result += string.Format("{0} 00000 n\n", page.BytePosition.ToString().PadLeft(10, '0'));
            }

            result += string.Format("{0} 00000 n\n", Properties.BytePosition.ToString().PadLeft(10, '0'));

            return result;
        }

        public int PositionObjects()
        {
            // need to sort the content elements on each page by z-Index (from 0 to n)
            // this is because the PDF renders the objects in the order they are defined
            foreach (var page in Pages)
            {
                page.Contents.Sort((Content x, Content y) =>
                {
                    if (x.ZIndex == 0 && y.ZIndex == 0) return 0;
                    else if (x.ZIndex == 0) return -1;
                    else if (y.ZIndex == 0) return 1;
                    else return x.ZIndex.CompareTo(y.ZIndex);
                });
            }

            foreach (var headerfooter in HeadersFooters)
            {
                headerfooter.Contents.Sort((Content x, Content y) =>
                {
                    if (x.ZIndex == 0 && y.ZIndex == 0) return 0;
                    else if (x.ZIndex == 0) return -1;
                    else if (y.ZIndex == 0) return 1;
                    else return x.ZIndex.CompareTo(y.ZIndex);
                });
            }

            var objectCount = 0;
            var fontCount = 0;
            var imageCount = 0;
            var contentCount = 0;
            var pageCount = 0;

            // Catalog is always the first object
            objectCount++;
            Catalog.Id = "C1";
            Catalog.ObjectNumber = objectCount;

            foreach (var font in Fonts)
            {
                fontCount++;
                objectCount++;
                font.Id = string.Format("F{0}", fontCount); 
                font.ObjectNumber = objectCount;
            }

            foreach (var image in Images)
            {
                imageCount++;
                objectCount++;
                image.Id = string.Format("I{0}", imageCount);
                image.ObjectNumber = objectCount;
            }

            // Allow for the parent page
            objectCount++;
            PageRoot.Id = "R1";
            PageRoot.ObjectNumber = objectCount;

            // The header and footer content
            foreach (var headerfooter in HeadersFooters)
            {
                foreach (var content in headerfooter.Contents)
                {
                    contentCount++;
                    objectCount++;
                    content.Id = string.Format("C{0}", contentCount);
                    content.ObjectNumber = objectCount;
                }
            }

            // Do the page content
            foreach (var page in Pages)
            {
                // Attach the page root to each page
                page.PageRoot = PageRoot;

                foreach (var content in page.Contents)
                {
                    contentCount++;
                    objectCount++;
                    content.Id = string.Format("C{0}", contentCount);
                    content.ObjectNumber = objectCount;
                }
            }

            // Do the pages
            foreach (var page in Pages)
            {
                pageCount++;
                objectCount++;
                page.Id = string.Format("P{0}", pageCount);
                page.ObjectNumber = objectCount;
            }

            // Document Info properties
            objectCount++;
            Properties.Id = "N1";
            Properties.ObjectNumber = objectCount;

            return objectCount;
        }

    }

}