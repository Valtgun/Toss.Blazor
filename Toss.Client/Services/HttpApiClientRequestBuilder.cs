﻿using Microsoft.AspNetCore.Components;

using Microsoft.AspNetCore.Components.Services;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Toss.Shared;

namespace Toss.Client.Services
{
    public class HttpApiClientRequestBuilder : IHttpApiClientRequestBuilder
    {
        private readonly string _uri;
        private readonly IUriHelper uriHelper;
        private readonly HttpClient _httpClient;
        private Func<HttpResponseMessage, Task> _onBadRequest;
        private Func<HttpResponseMessage, Task> _onOK;
        private readonly IBrowserCookieService browserCookieService;
        private IMessageService messageService;
        public IJsInterop JsInterop { get; }

        public HttpApiClientRequestBuilder(HttpClient httpClient,
            string uri, 
            IUriHelper uriHelper, 
            IBrowserCookieService browserCookieService, IJsInterop jsInterop, IMessageService messageService)
        {
            _uri = uri;
            this.uriHelper = uriHelper;
            _httpClient = httpClient;

            this.browserCookieService = browserCookieService;
            JsInterop = jsInterop;
            this.messageService = messageService;
        }

        public async Task Post(byte[] data)
        {
            await ExecuteHttpQuery(async () =>
            {
                return await _httpClient.SendAsync(await PrepareMessageAsync(new HttpRequestMessage(HttpMethod.Post, _uri)
                {
                    Content = new ByteArrayContent(data)
                }));
            });
        }
        public async Task Post<T>(T data)
        {
            await SetCaptchaToken(data);
            await ExecuteHttpQuery(async () =>
            {
                var requestJson = Json.Serialize(data);
                return await _httpClient.SendAsync(await PrepareMessageAsync(new HttpRequestMessage(HttpMethod.Post, _uri)
                {
                    Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json")
                }));
            });
        }

        private async Task SetCaptchaToken<T>(T data)
        {
            if (data is NotARobot)
            {
                (data as NotARobot).Token = await JsInterop.Captcha(this._uri);
            }
        }

        public async Task Post()
        {
            await ExecuteHttpQuery(async () => await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, _uri)));
        }
        private async Task HandleHttpResponse(HttpResponseMessage response)
        {
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.OK:
                    if (_onOK != null)
                        await _onOK(response);
                    break;
                case System.Net.HttpStatusCode.BadRequest:
                    if (_onBadRequest != null)
                        await _onBadRequest(response);
                    break;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    break;
                case System.Net.HttpStatusCode.InternalServerError:
                    messageService.Error( "A server error occured, sorry");
                    break;
                    //other case , we do nothing, I'll add this case as needed
            }
        }
        private async Task<HttpRequestMessage> PrepareMessageAsync(HttpRequestMessage httpRequestMessage)
        {
            string csrfCookieValue = await browserCookieService.Get(c => c.Equals("CSRF-TOKEN"));
            if (csrfCookieValue != null)
                httpRequestMessage.Headers.Add("X-CSRF-TOKEN", csrfCookieValue);
            return httpRequestMessage;
        }
        public async Task Get()
        {
            await ExecuteHttpQuery(async () => await _httpClient.SendAsync(await PrepareMessageAsync(new HttpRequestMessage(HttpMethod.Get, _uri))));
        }
        private async Task ExecuteHttpQuery(Func<Task<HttpResponseMessage>> httpCall)
        {
            messageService.Loading();
            try
            {
                var response = await httpCall();
                await HandleHttpResponse(response);
            }
            catch
            {
                messageService.Error( "Connection error, server is down or you are not connected to the same network.");
                throw;
            }
            finally
            {
                messageService.LoadingDone();
            }
        }
        public HttpApiClientRequestBuilder OnBadRequest<T>(Action<T> todo)
        {
            _onBadRequest = async (HttpResponseMessage r) =>
            {
                var response = Json.Deserialize<T>(await r.Content.ReadAsStringAsync());
                todo(response);
            };
            return this;
        }
        public HttpApiClientRequestBuilder OnOK<T>(Action<T> todo)
        {
            _onOK = async (HttpResponseMessage r) =>
            {
                var response = Json.Deserialize<T>(await r.Content.ReadAsStringAsync());
                todo(response);
            };
            return this;
        }
        public HttpApiClientRequestBuilder OnOK(Func<Task> todo)
        {
            _onOK = async (HttpResponseMessage r) =>
            {
                await todo();
            };
            return this;
        }
        public HttpApiClientRequestBuilder OnBadRequest(Func<Task> todo)
        {
            _onBadRequest = async (HttpResponseMessage r) =>
            {
                await todo();
            };
            return this;
        }
        public HttpApiClientRequestBuilder OnOK(Action todo)
        {
            _onOK = (HttpResponseMessage r) =>
            {
                todo();
                return Task.CompletedTask;
            };
            return this;
        }
        public HttpApiClientRequestBuilder OnBadRequest(Action todo)
        {
            _onBadRequest = (HttpResponseMessage r) =>
            {
                todo();
                return Task.CompletedTask;
            };
            return this;
        }
        public HttpApiClientRequestBuilder OnOK(string successMessage, string navigateTo = null)
        {
            OnOK( () =>
            {
                if (!string.IsNullOrEmpty(successMessage))
                    messageService.Info( successMessage);
                if (!string.IsNullOrEmpty(navigateTo))
                    uriHelper.NavigateTo(navigateTo);
            });
            return this;
        }

        public void SetHeader(string key, string value)
        {
            _httpClient.DefaultRequestHeaders.Add(key, value);
        }
    }
}
