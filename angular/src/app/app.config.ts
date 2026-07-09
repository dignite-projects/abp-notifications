import { provideAbpCore, withOptions } from '@abp/ng.core';
import { provideAbpOAuth } from '@abp/ng.oauth';
import { provideSettingManagementConfig } from '@abp/ng.setting-management/config';
import { provideFeatureManagementConfig } from '@abp/ng.feature-management';
import { provideAbpThemeShared,  } from '@abp/ng.theme.shared';
import { provideIdentityConfig } from '@abp/ng.identity/config';
import { provideAccountConfig } from '@abp/ng.account/config';
import { registerLocaleForEsBuild } from '@abp/ng.core/locale';
import { provideThemeLeptonX } from '@abp/ng.theme.lepton-x';
import { provideSideMenuLayout } from '@abp/ng.theme.lepton-x/layouts';
import { provideLogo, withEnvironmentOptions } from "@abp/ng.theme.shared";
import { FooterLinksService } from '@volo/ngx-lepton-x.core';
import { ApplicationConfig, inject, provideAppInitializer } from '@angular/core';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { environment } from '../environments/environment';
import { APP_ROUTES } from './app.routes';
import { APP_ROUTE_PROVIDER } from './route.provider';
import { provideNotificationCenterConfig } from '@dignite-abp/notification-center/config';

const DIGNITE_REPO_URL = 'https://github.com/dignite-projects/abp-notifications';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(APP_ROUTES),
    APP_ROUTE_PROVIDER,
    provideAppInitializer(() => {
      inject(FooterLinksService).setFooterInfo({
        brandName: '',
        brandUrl: '',
        authorName: 'Dignite',
        authorUrl: 'https://dignite.com',
        links: [{ text: 'About', link: DIGNITE_REPO_URL }],
      });
    }),
    provideAnimations(),
    provideAbpCore(
      withOptions({
        environment,
        registerLocaleFn: registerLocaleForEsBuild(),
      }),
    ),
    provideAbpOAuth(),
    provideIdentityConfig(),
    provideSettingManagementConfig(),
    provideFeatureManagementConfig(),
    provideAccountConfig(),
    provideAbpThemeShared(),
    provideThemeLeptonX(),
    provideSideMenuLayout(),
    provideLogo(withEnvironmentOptions(environment)),
    provideNotificationCenterConfig(),
  ]
};
