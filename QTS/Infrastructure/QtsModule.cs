using Autofac;
using Microsoft.AspNetCore.Mvc;
using QTS.Infrastructure.Auth;
using QTS.Infrastructure.Okx;
using QTS.Services;
using QTS.Strategies;

namespace QTS.Infrastructure;

/// <summary>Autofac 依赖注册：服务、策略、欧易客户端与控制器。</summary>
public sealed class QtsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var assembly = typeof(QtsModule).Assembly;

        builder.RegisterType<PasswordHasher>().AsSelf().SingleInstance();
        builder.RegisterType<TokenService>().As<ITokenService>().SingleInstance();
        builder.RegisterType<SecretProtector>().As<ISecretProtector>().SingleInstance();

        // 业务服务（QTS.Services 下的实现类），默认按请求生命周期注册。
        builder.RegisterAssemblyTypes(assembly)
            .Where(t => t.Namespace == "QTS.Services" && t.IsClass && !t.IsAbstract)
            .AsSelf()
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        // OKX 配置需要跨请求缓存，使用单例覆盖上面的注册。
        builder.RegisterType<OkxConfigService>().As<IOkxConfigService>().AsSelf().SingleInstance();

        // 策略与注册表。
        builder.RegisterAssemblyTypes(assembly)
            .Where(t => typeof(IStrategy).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .As<IStrategy>()
            .SingleInstance();

        builder.RegisterAssemblyTypes(assembly)
            .Where(t => typeof(IStrategyRegistry).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .As<IStrategyRegistry>()
            .SingleInstance();

        // 欧易 API 客户端（无状态，单例即可）。
        builder.RegisterAssemblyTypes(assembly)
            .Where(t => typeof(IOkxClient).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .As<IOkxClient>()
            .SingleInstance();

        // 控制器交给 Autofac 解析。
        builder.RegisterAssemblyTypes(assembly)
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .InstancePerLifetimeScope();
    }
}
