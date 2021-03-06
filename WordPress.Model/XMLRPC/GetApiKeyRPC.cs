﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Xml.Linq;

namespace WordPress.Model
{
    public class GetApiKeyRPC
    {
        //DEVNOTE: ideally this would be a subclass of XmlRemoteProcedureCall, but the
        //request continually fails(cryptic 'not found' exceptions) even though the URI is valid.

        #region member variables

        public Blog blog;
        private SendOrPostCallback onCompletedDelegate;
        private bool selfHosted;
        
        #endregion

        #region events

        public event XMLRPCCompletedEventHandler<Blog> Completed;

        #endregion


        #region constructors

        public GetApiKeyRPC(Blog aBlog, bool selfHostedBlog)
        {
            selfHosted = selfHostedBlog;
            blog = aBlog;
            onCompletedDelegate = new SendOrPostCallback(NotifyCompleted);
        }

        #endregion

        #region properties

        #endregion

        #region methods
        
        private void CompletionMethod(List<Blog> items, Exception exception, bool canceled, AsyncOperation asyncOp)
        {            
            //package the results of the operation in an XMLRPCCompletedEventArgs object
            XMLRPCCompletedEventArgs<Blog> args = new XMLRPCCompletedEventArgs<Blog>(items, exception, canceled, asyncOp.UserSuppliedState);

            asyncOp.PostOperationCompleted(onCompletedDelegate, args);
        }
        
        private void NotifyCompleted(object state)
        {
            XMLRPCCompletedEventArgs<Blog> args = state as XMLRPCCompletedEventArgs<Blog>;
            if (null != Completed)
            {
                Completed(this, args);
            }
        }

        public void ExecuteAsync()
        {
            ExecuteAsync(Guid.NewGuid());
        }

        public void ExecuteAsync(object taskId)
        {
            ValidateValues();

            AsyncOperation operation = AsyncOperationManager.CreateOperation(taskId);

            //start the async op
            ThreadPool.QueueUserWorkItem((state) =>
            {
                RequestKey(operation);
            });
        }

        private void ValidateValues()
        {
            if (null == blog)
            {
                throw new ArgumentException("Blog may not be null", "Blog");
            }
            if (string.IsNullOrEmpty(blog.Username))
            {
                throw new ArgumentException("Blog.Username may not be null or an empty string", "Blog");
            }
            if (string.IsNullOrEmpty(blog.Password))
            {
                throw new ArgumentException("Blog.Password may not be null or an empty string.", "Blog");
            }
        }

        private void RequestKey(AsyncOperation operation)
        {

            bool hasNetworkConnection = NetworkInterface.GetIsNetworkAvailable();
            if (!hasNetworkConnection)
            {
                Exception connErr = new NoConnectionException();
                CompletionMethod(null, connErr, false, operation);
                return;
            }

            HttpWebRequest request = HttpWebRequest.Create(Constants.WORDPRESS_APIKEY_URL) as HttpWebRequest;
            request.AllowAutoRedirect = true;
            request.ContentType = XmlRPCRequestConstants.CONTENTTYPE;
            request.Method = XmlRPCRequestConstants.POST;
            request.UserAgent = Constants.WORDPRESS_USERAGENT;
            if (selfHosted)
            {
                request.Credentials = new NetworkCredential(blog.DotcomUsername, blog.DotcomPassword);
            }
            else
            {
                request.Credentials = new NetworkCredential(blog.Username, blog.Password);
            }
           

            request.BeginGetResponse(responseResult =>
            {
                try
                {
                    HttpWebResponse response = request.EndGetResponse(responseResult) as HttpWebResponse;
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        string responseContent = reader.ReadToEnd();
                        XDocument xDoc = ParseDocument(responseContent);

                        Exception exception = null;
                        List<Blog> items = null;

                        var fault = xDoc.Descendants().Where(element => XmlRPCResponseConstants.NAME == element.Name && XmlRPCResponseConstants.FAULTCODE_VALUE == element.Value);
                        if (null != fault && 0 < fault.Count())
                        {
                            exception = ParseFailureInfo(xDoc.Descendants(XmlRPCResponseConstants.STRUCT).First());                            
                        }
                        else
                        {
                            try
                            {
                                items = ParseResponseContent(xDoc);
                            }
                            catch (Exception ex)
                            {
                                exception = new XmlRPCParserException(XmlRPCResponseConstants.XELEMENTMISSINGCHILDELEMENTS_CODE, XmlRPCResponseConstants.XELEMENTMISSINGCHILDELEMENTS_MESSAGE, ex);
                            }
                        }

                        CompletionMethod(items, exception, false, operation);
                    }
                }
                catch (Exception ex)
                {
                    CompletionMethod(new List<Blog>(), ex, false, operation);
                }

            }, request);
        }

        private XDocument ParseDocument(string content)
        {
            XDocument xDoc = null;
            try
            {
                xDoc = XDocument.Parse(content, LoadOptions.None);                
            }
            catch (Exception ex)
            {
                throw new XmlRPCParserException(XmlRPCResponseConstants.SERVER_RETURNED_INVALID_XML_RPC_CODE, XmlRPCResponseConstants.SERVER_RETURNED_INVALID_XML_RPC_MESSAGE, ex);
            }
            return xDoc;
        }

        private Exception ParseFailureInfo(XElement element)
        {
            int faultCode = -1;
            string message = string.Empty;

            XElement valueElement = null;
            foreach (XElement nameElement in element.Descendants(XmlRPCResponseConstants.NAME))
            {
                if (XmlRPCResponseConstants.FAULTCODE_VALUE.Equals(nameElement.Value))
                {
                    valueElement = ((XElement)nameElement.NextNode).DescendantNodes().First() as XElement;
                    if (!int.TryParse(valueElement.Value, out faultCode))
                    {
                        return new ArgumentException("Unable to parse fault code from response");
                    }
                }
                else if (XmlRPCResponseConstants.FAULTSTRING_VALUE.Equals(nameElement.Value))
                {
                    valueElement = ((XElement)nameElement.NextNode).DescendantNodes().First() as XElement;
                    message = valueElement.Value;
                }
            }

            return new XmlRPCException(faultCode, message);
        }

        private List<Blog> ParseResponseContent(XDocument xDoc)
        {
            string key = xDoc.Descendants(XmlRPCResponseConstants.APIKEY).First().Value;
            List<Blog> result = new List<Blog>();
            blog.ApiKey = key;
            result.Add(blog);
            return result;
        }

        #endregion

    }
}
