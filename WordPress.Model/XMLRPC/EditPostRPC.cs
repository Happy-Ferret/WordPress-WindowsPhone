﻿using System;
using System.Net;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Collections.Generic;

namespace WordPress.Model
{
    /// <summary>
    /// Used to edit both posts and pages
    /// </summary>
    public class EditPostRPC: XmlRemoteProcedureCall<Post>
    {
        #region member variables

        private readonly string _content;

        private const string METHODNAME_VALUE = "metaWeblog.editPost";

        #endregion

        #region constructors

        public EditPostRPC()
            : base()
        {
            _content = XMLRPCTable.metaWeblog_editPost;
            MethodName = "metaWeblog.editPost";
        }

        public EditPostRPC(Blog blog, Post post)
            : base(blog.Xmlrpc, METHODNAME_VALUE, blog.Username, blog.Password)
        {
            _content = XMLRPCTable.metaWeblog_editPost;
            Post = post;
        }

        #endregion

        #region properties

        public Post Post { get; set; }

        public bool Publish { get; set; }

        public ePostType PostType { get; set; }
        
        #endregion

        #region methods

        private string FormatCategories()
        {
            string dataFormatString = "<value><string>{0}</string></value>";

            StringBuilder categoryBuilder = new StringBuilder();
            string data = string.Empty;

            foreach (string category in Post.Categories)
            {
                data = string.Format(dataFormatString, category.HtmlEncode());
                categoryBuilder.Append(data);
            }

            return categoryBuilder.ToString();
        }

        protected override void ValidateValues()
        {
            base.ValidateValues();

            if (null == Post)
            {
                throw new ArgumentException("Post may not be null", "Post");
            }
        }

        protected override string BuildPostContentString()
        {
            string status = "";
            if (Publish)
            {
                status = "publish";
            }
            else
            {
                status = "draft";
            }
            string result = string.Format(_content,
                Post.PostId,
                Credentials.UserName.HtmlEncode(),
                Credentials.Password.HtmlEncode(),
                Post.MtKeyWords.HtmlEncode(),
                FormatCategories(),
                Post.Title.HtmlEncode(),
                Post.Description.HtmlEncode(),
                PostType.ToString(),
                status);

            return result;
        }

        protected override List<Post> ParseResponseContent(XDocument xDoc)
        {
            XElement booleanElement = xDoc.Descendants(XmlRPCResponseConstants.BOOLEAN).First();

            if (XmlRPCResponseConstants.FALSE_STRING != booleanElement.Value)
            {
                List<Post> result = new List<Post>();
                result.Add(Post);
                return result;
            }
            else
            {
                XmlRPCException exception = new XmlRPCException(10000, XmlRPCResponseConstants.XML_RPC_OPERATION_FAILED);
                throw exception;
            }
        }

        #endregion


    }
}
