﻿using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace WebApiClient
{
    /// <summary>
    /// 表示HttpApi创建工厂
    /// 提供HttpApi的配置注册和实例创建
    /// 并对实例的生命周期进行自动管理
    /// </summary>
    public class HttpApiFactory<TInterface> : IHttpApiFactory<TInterface>, IHttpApiFactory
        where TInterface : class, IHttpApi
    {
        /// <summary>
        /// HttpApiConfig的配置委托
        /// </summary>
        private Action<HttpApiConfig> configAction;

        /// <summary>
        /// HttpMessageHandler的创建委托
        /// </summary>
        private Func<HttpMessageHandler> handlerFunc;

        /// <summary>
        /// handler的生命周期
        /// </summary>
        private TimeSpan lifeTime = TimeSpan.FromMinutes(2d);

        /// <summary>
        /// 具有生命周期的httpHandler延时创建对象
        /// </summary>
        private Lazy<LifetimeHttpHandler> lifeTimeHttpHandlerLazy;


        /// <summary>
        /// cookie容器
        /// </summary>
        private CookieContainer cookieContainer;

        /// <summary>
        /// 是否保持cookie容器
        /// </summary>
        private bool keepCookieContainer = HttpHandlerProvider.IsSupported;

        /// <summary>
        /// HttpHandler清理器
        /// </summary>
        private readonly LifetimeHttpHandlerCleaner httpHandlerCleaner = new LifetimeHttpHandlerCleaner();

        /// <summary>
        /// 获取handler的生命周期
        /// </summary>
        public TimeSpan LifeTime
        {
            get => this.lifeTime;
        }

        /// <summary>
        /// 获取是否保持cookie容器
        /// </summary>
        public bool KeepCookieContainer
        {
            get => this.keepCookieContainer;
        }

        /// <summary>
        /// HttpApi创建工厂
        /// </summary>
        public HttpApiFactory()
        {
            this.lifeTimeHttpHandlerLazy = new Lazy<LifetimeHttpHandler>(
                this.CreateHttpHandler,
                true);
        }

        /// <summary>
        /// 置HttpApi实例的生命周期
        /// </summary>
        /// <param name="lifeTime">生命周期</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public HttpApiFactory<TInterface> SetLifetime(TimeSpan lifeTime)
        {
            if (lifeTime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException();
            }
            this.lifeTime = lifeTime;
            return this;
        }

        /// <summary>
        /// 获取或设置清理过期的HttpApi实例的时间间隔
        /// </summary>
        /// <param name="interval">时间间隔</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public HttpApiFactory<TInterface> SetCleanupInterval(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException();
            }
            this.httpHandlerCleaner.CleanupInterval = interval;
            return this;
        }

        /// <summary>
        /// 设置是否维护使用一个CookieContainer实例
        /// 该实例为首次创建时的CookieContainer
        /// </summary>
        /// <param name="keep">true维护使用一个CookieContainer实例</param>
        /// <exception cref="PlatformNotSupportedException"></exception>
        /// <returns></returns>
        public HttpApiFactory<TInterface> SetKeepCookieContainer(bool keep)
        {
            if (keep == true && HttpHandlerProvider.IsSupported == false)
            {
                var message = $"无法设置KeepCookieContainer，请在{nameof(ConfigureHttpMessageHandler)}为Handler设置固定的{nameof(CookieContainer)}";
                throw new PlatformNotSupportedException(message);
            }

            this.keepCookieContainer = keep;
            return this;
        }

        /// <summary>
        /// 配置HttpMessageHandler的创建
        /// </summary>
        /// <param name="handlerFunc">创建委托</param>
        /// <returns></returns>
        public HttpApiFactory<TInterface> ConfigureHttpMessageHandler(Func<HttpMessageHandler> handlerFunc)
        {
            this.handlerFunc = handlerFunc;
            return this;
        }

        /// <summary>
        /// 配置HttpApiConfig
        /// </summary>
        /// <param name="configAction">配置委托</param>
        /// <returns></returns>
        public HttpApiFactory<TInterface> ConfigureHttpApiConfig(Action<HttpApiConfig> configAction)
        {
            this.configAction = configAction;
            return this;
        }

        /// <summary>
        /// 配置HttpApiConfig
        /// </summary>
        /// <param name="configAction">配置委托</param>
        void IHttpApiFactory<TInterface>.ConfigureHttpApiConfig(Action<HttpApiConfig> configAction)
        {
            this.configAction = configAction;
        }

        /// <summary>
        /// 创建接口的代理实例
        /// </summary>
        /// <returns></returns>
        public TInterface CreateHttpApi()
        {
            return ((IHttpApiFactory)this).CreateHttpApi() as TInterface;
        }

        /// <summary>
        /// 创建接口的代理实例
        /// </summary>
        /// <returns></returns>
        HttpApiClient IHttpApiFactory.CreateHttpApi()
        {
            var handler = this.lifeTimeHttpHandlerLazy.Value;
            var httpApiConfig = new LifetimeHttpApiConfig(handler);

            if (this.configAction != null)
            {
                this.configAction.Invoke(httpApiConfig);
            }

            if (this.keepCookieContainer == true)
            {
                Interlocked.CompareExchange(ref this.cookieContainer, httpApiConfig.HttpHandler.CookieContainer, null);
                if (httpApiConfig.HttpHandler.CookieContainer != this.cookieContainer)
                {
                    httpApiConfig.HttpHandler.CookieContainer = this.cookieContainer;
                }
            }

            return HttpApiClient.Create(typeof(TInterface), httpApiConfig);
        }

        /// <summary>
        /// 创建LifetimeHttpHandler
        /// </summary>
        /// <returns></returns>
        private LifetimeHttpHandler CreateHttpHandler()
        {
            var handler = this.handlerFunc?.Invoke() ?? new DefaultHttpClientHandler();
            return new LifetimeHttpHandler(handler, this.lifeTime, this.OnHttpHandlerDeactivate);
        }

        /// <summary>
        /// 当有httpHandler失效时
        /// </summary>
        /// <param name="handler">httpHandler</param>
        private void OnHttpHandlerDeactivate(LifetimeHttpHandler handler)
        {
            // 切换激活状态的记录的实例
            this.lifeTimeHttpHandlerLazy = new Lazy<LifetimeHttpHandler>(this.CreateHttpHandler, true);
            this.httpHandlerCleaner.Add(handler);
        }
    }
}