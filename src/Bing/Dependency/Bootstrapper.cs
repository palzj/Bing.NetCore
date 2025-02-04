﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AspectCore.Configuration;
using Autofac;
using Bing.Events.Handlers;
using Bing.Helpers;
using Bing.Reflections;
using Bing.Utils.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Bing.Dependency
{
    /// <summary>
    /// 依赖引导器
    /// </summary>
    public class Bootstrapper
    {
        /// <summary>
        /// 服务集合
        /// </summary>
        private readonly IServiceCollection _services;

        /// <summary>
        /// 依赖配置
        /// </summary>
        private readonly IConfig[] _configs;

        /// <summary>
        /// 容器生成器
        /// </summary>
        private ContainerBuilder _builder;

        /// <summary>
        /// 类型查找器
        /// </summary>
        private ITypeFinder _finder;

        /// <summary>
        /// 所有程序集查找器
        /// </summary>
        private readonly IAllAssemblyFinder _allAssemblyFinder;
        
        /// <summary>
        /// 程序集列表
        /// </summary>
        private List<Assembly> _assemblies;

        /// <summary>
        /// Aop配置操作
        /// </summary>
        private readonly Action<IAspectConfiguration> _aopConfigAction;

        /// <summary>
        /// 初始化一个<see cref="Bootstrapper"/>类型的实例
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configs">依赖配置</param>
        /// <param name="aopConfigAction">Aop配置操作</param>
        /// <param name="finder">类型查找器</param>
        public Bootstrapper(IServiceCollection services, IConfig[] configs,
            Action<IAspectConfiguration> aopConfigAction, ITypeFinder finder)
        {
            _services = services ?? new ServiceCollection();
            _configs = configs;
            _aopConfigAction = aopConfigAction;
            _allAssemblyFinder = new AppDomainAllAssemblyFinder();
            _finder = finder ?? new DependencyTypeFinder(_allAssemblyFinder);
        }

        /// <summary>
        /// 启动引导
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configs">依赖配置</param>
        /// <param name="aopConfigAction">Aop配置操作</param>
        /// <param name="finder">类型查找器</param>
        /// <returns></returns>
        public static IServiceProvider Run(IServiceCollection services = null, IConfig[] configs = null,
            Action<IAspectConfiguration> aopConfigAction = null, ITypeFinder finder = null)
        {
            return new Bootstrapper(services, configs, aopConfigAction, finder).Bootstrap();
        }

        /// <summary>
        /// 启动引导
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configs">依赖配置</param>
        /// <returns></returns>
        public static IServiceProvider Run(IServiceCollection services, params IConfig[] configs)
        {
            return Run(services, configs, null);
        }

        /// <summary>
        /// 引导
        /// </summary>
        /// <returns></returns>
        public IServiceProvider Bootstrap()
        {
            _assemblies = _allAssemblyFinder.FindAll(true).ToList();
            return Ioc.DefaultContainer.Register(_services, RegistServices, _configs);
        }

        /// <summary>
        /// 注册服务集合
        /// </summary>
        /// <param name="builder">容器生成器</param>
        private void RegistServices(ContainerBuilder builder)
        {
            _builder = builder;
            RegistInfrastracture();
            RegistEventHandlers();
            RegistDependency();
        }

        /// <summary>
        /// 注册基础设施
        /// </summary>
        private void RegistInfrastracture()
        {
            EnableAop();
            RegistFinder();
        }

        /// <summary>
        /// 启用Aop
        /// </summary>
        private void EnableAop()
        {
            _builder.EnableAop(_aopConfigAction);
        }

        /// <summary>
        /// 注册类型查找器
        /// </summary>
        private void RegistFinder()
        {
            _builder.AddSingleton(_finder);
        }

        /// <summary>
        /// 注册事件处理器
        /// </summary>
        private void RegistEventHandlers()
        {
            RegistEventHandlers(typeof(IEventHandler<>));
        }

        /// <summary>
        /// 注册事件处理器
        /// </summary>
        /// <param name="handlerType">处理器类型</param>
        private void RegistEventHandlers(Type handlerType)
        {
            var handlerTypes = GetTypes(handlerType);
            foreach (var handler in handlerTypes)
            {
                _builder.RegisterType(handler)
                    .As(handler.FindInterfaces(
                        (filter, criteria) => filter.IsGenericType &&
                                              ((Type) criteria).IsAssignableFrom(filter.GetGenericTypeDefinition()),
                        handlerType)).InstancePerLifetimeScope();
            }
        }

        /// <summary>
        /// 查找并注册依赖
        /// </summary>
        private void RegistDependency()
        {
            RegistSingletonDependency();
            RegistScopeDependency();
            RegistTransientDependency();
            ResolveDependencyRegistrar();
        }

        /// <summary>
        /// 注册单例依赖
        /// </summary>
        private void RegistSingletonDependency()
        {
            _builder.RegisterTypes(GetTypes<ISingletonDependency>()).AsImplementedInterfaces().PropertiesAutowired()
                .SingleInstance();
        }

        /// <summary>
        /// 注册作用域依赖
        /// </summary>
        private void RegistScopeDependency()
        {
            _builder.RegisterTypes(GetTypes<IScopeDependency>()).AsImplementedInterfaces().PropertiesAutowired()
                .InstancePerLifetimeScope();
        }

        /// <summary>
        /// 注册瞬态依赖
        /// </summary>
        private void RegistTransientDependency()
        {
            _builder.RegisterTypes(GetTypes<ITransientDependency>()).AsImplementedInterfaces().PropertiesAutowired()
                .InstancePerDependency();
        }

        /// <summary>
        /// 解析依赖注册器
        /// </summary>
        private void ResolveDependencyRegistrar()
        {
            var types = GetTypes<IDependencyRegistrar>();
            types.Select(type => Reflection.CreateInstance<IDependencyRegistrar>(type)).ToList()
                .ForEach(t => t.Register(_services));
        }

        /// <summary>
        /// 获取类型集合
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <returns></returns>
        private Type[] GetTypes<T>()
        {
            return _finder.Find<T>(_assemblies).ToArray();
        }

        /// <summary>
        /// 获取类型集合
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns></returns>
        private Type[] GetTypes(Type type)
        {
            return _finder.Find(type, _assemblies).ToArray();
        }
    }
}
