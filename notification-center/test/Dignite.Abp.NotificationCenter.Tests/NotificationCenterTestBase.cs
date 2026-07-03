using System;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.Testing;
using Volo.Abp.Uow;

namespace Dignite.Abp.NotificationCenter;

public abstract class NotificationCenterTestBase : AbpIntegratedTest<NotificationCenterTestModule>
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }

    protected virtual async Task WithUnitOfWorkAsync(Func<Task> func)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await func();
        await uow.CompleteAsync();
    }
}
