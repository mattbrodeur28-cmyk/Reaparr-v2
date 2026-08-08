<template>
	<q-drawer
		class="navigation-drawer v5-navigation-drawer"
		:model-value="showDrawer"
		:width="336"
		:breakpoint="960"
		@before-show="onShow"
		@before-hide="onHide">
		<div class="v5-drawer-shell">
			<div class="v5-drawer-heading">
				<div>
					<div class="v5-drawer-eyebrow">
						Workspace
					</div>
					<div class="v5-drawer-title">
						Media control
					</div>
				</div>
				<div class="v5-drawer-orb">
					<q-icon name="mdi-access-point-network" />
				</div>
			</div>

			<nav class="v5-primary-nav">
				<q-item
					v-ripple
					clickable
					to="/discover"
					class="v5-primary-nav__item"
					:active="route.path.startsWith('/discover')"
					active-class="v5-primary-nav__item--active">
					<q-item-section avatar>
						<div class="v5-primary-nav__icon">
							<q-icon name="mdi-compass-rose" />
						</div>
					</q-item-section>
					<q-item-section>
						<q-item-label>Discover</q-item-label>
						<q-item-label caption>
							Missing, incomplete & upgrades
						</q-item-label>
					</q-item-section>
					<q-item-section side>
						<q-icon name="mdi-chevron-right" />
					</q-item-section>
				</q-item>

				<q-item
					v-ripple
					clickable
					to="/downloads"
					class="v5-primary-nav__item"
					:active="route.path.startsWith('/downloads')"
					active-class="v5-primary-nav__item--active">
					<q-item-section avatar>
						<div class="v5-primary-nav__icon">
							<q-icon name="mdi-download-circle-outline" />
						</div>
					</q-item-section>
					<q-item-section>
						<q-item-label>
							{{ t('components.navigation-drawer.downloads') }}
						</q-item-label>
						<q-item-label caption>
							Queue and transfer activity
						</q-item-label>
					</q-item-section>
					<q-item-section side>
						<q-badge
							v-if="downloadStore.getActiveDownloadList().length"
							rounded
							color="primary"
							:label="downloadStore.getActiveDownloadList().length" />
						<q-icon
							v-else
							name="mdi-chevron-right" />
					</q-item-section>
				</q-item>
			</nav>

			<div class="v5-drawer-section-header">
				<span>Libraries</span>
				<q-icon name="mdi-server-network" />
			</div>

			<QCol class="server-drawer-container v5-server-drawer">
				<q-scroll>
					<ServerDrawer />
				</q-scroll>
			</QCol>

			<div class="v5-drawer-footer">
				<div class="v5-drawer-section-header v5-drawer-section-header--footer">
					<span>System</span>
					<q-icon name="mdi-tune-variant" />
				</div>
				<QExpansionList :items="secondaryNavItems" />
			</div>
		</div>
	</q-drawer>
</template>

<script setup lang="ts">
import type { QExpansionListProps } from '@interfaces/components/QExpansionListProps';
import { useSettingsStore, useDownloadStore } from '@store';
import { useI18n } from 'vue-i18n';

withDefaults(defineProps<{ showDrawer?: boolean }>(), {
	showDrawer: false,
});

const route = useRoute();
const settingsStore = useSettingsStore();
const downloadStore = useDownloadStore();
const { t } = useI18n();

const secondaryNavItems = computed((): QExpansionListProps[] => {
	const items: QExpansionListProps[] = [
		{
			title: t('components.navigation-drawer.settings'),
			icon: 'mdi-tune-variant',
			link: '/settings',
			children: [
				{
					title: t('components.navigation-drawer.accounts'),
					icon: 'mdi-account',
					link: '/settings/accounts',
				},
				{
					title: t('components.navigation-drawer.paths'),
					icon: 'mdi-folder',
					link: '/settings/paths',
				},
				{
					title: t('components.navigation-drawer.ui'),
					icon: 'mdi-monitor-dashboard',
					link: '/settings/ui',
				},
				{
					title: t('components.navigation-drawer.integrations'),
					icon: 'mdi-connection',
					link: '/settings/integrations',
				},
				{
					title: t('components.navigation-drawer.advanced'),
					icon: 'mdi-cog-outline',
					link: '/settings/advanced',
				},
				{
					title: t('components.navigation-drawer.logs'),
					icon: 'mdi-text-box-search-outline',
					link: '/settings/logs',
				},
			],
		},
	];

	if (settingsStore.debugMode) {
		items.push({
			title: t('components.navigation-drawer.debug'),
			icon: 'mdi-bug-outline',
			children: [
				{
					title: t('components.navigation-drawer.scratchpad'),
					icon: 'mdi-note-edit',
					link: '/debug-pages/scratchpad',
				},
				{
					title: t('components.navigation-drawer.dialogs'),
					icon: 'mdi-dock-window',
					link: '/debug-pages/dialogs',
				},
				{
					title: t('components.navigation-drawer.buttons'),
					icon: 'mdi-button-pointer',
					link: '/debug-pages/buttons',
				},
			],
		});
	}

	return items;
});

function onShow() {
	document.body.classList.remove('navigation-drawer-closed');
	document.body.classList.add('navigation-drawer-opened');
}

function onHide() {
	document.body.classList.remove('navigation-drawer-opened');
	document.body.classList.add('navigation-drawer-closed');
}

onMounted(() => {
	document.body.classList.add('navigation-drawer-opened');
});
</script>

<style lang="scss">
.v5-navigation-drawer {
  border-right: 0 !important;
  background: transparent !important;
}

.v5-navigation-drawer .q-drawer__content {
  padding: 10px 0 10px 10px;
  background: transparent;
}

.v5-drawer-shell {
  display: flex;
  height: 100%;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid var(--v5-border);
  border-radius: 24px;
  background: var(--v5-surface-strong);
  box-shadow: var(--v5-shadow-lg);
  backdrop-filter: blur(26px) saturate(150%);
}

.v5-drawer-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 21px 19px 13px;
}

.v5-drawer-eyebrow,
.v5-drawer-section-header {
  color: var(--v5-text-muted);
  font-size: 0.68rem;
  font-weight: 800;
  letter-spacing: 0.13em;
  text-transform: uppercase;
}

.v5-drawer-title {
  margin-top: 3px;
  font-size: 1.22rem;
  font-weight: 800;
  letter-spacing: -0.025em;
}

.v5-drawer-orb {
  display: grid;
  width: 40px;
  height: 40px;
  place-items: center;
  border: 1px solid rgba(255, 91, 118, 0.22);
  border-radius: 14px;
  background: linear-gradient(135deg, rgba(255, 67, 105, 0.18), rgba(118, 79, 255, 0.13));
  color: #ff7892;
}

.v5-primary-nav {
  display: grid;
  gap: 7px;
  padding: 4px 10px 14px;
}

.v5-primary-nav__item {
  min-height: 66px;
  border: 1px solid transparent;
  border-radius: 17px;
  color: var(--v5-text-muted);
  transition:
    background 170ms ease,
    border-color 170ms ease,
    transform 170ms ease,
    color 170ms ease;
}

.v5-primary-nav__item:hover {
  background: var(--v5-surface-soft);
  color: var(--v5-text);
  transform: translateX(2px);
}

.v5-primary-nav__item--active {
  border-color: rgba(255, 78, 112, 0.25);
  background:
    linear-gradient(90deg, rgba(255, 70, 105, 0.17), rgba(111, 77, 255, 0.07)) !important;
  color: var(--v5-text) !important;
}

.v5-primary-nav__icon {
  display: grid;
  width: 38px;
  height: 38px;
  place-items: center;
  border-radius: 13px;
  background: var(--v5-surface-soft);
  color: var(--v5-text-muted);
  font-size: 1.12rem;
}

.v5-primary-nav__item--active .v5-primary-nav__icon {
  background: linear-gradient(135deg, #ff466a, #ff6c4d);
  color: white;
  box-shadow: 0 8px 22px rgba(255, 70, 106, 0.23);
}

.v5-primary-nav__item .q-item__label--caption {
  margin-top: 2px;
  color: var(--v5-text-subtle);
  font-size: 0.7rem;
}

.v5-drawer-section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 11px 18px 9px;
}

.v5-server-drawer {
  min-height: 0;
  flex: 1 1 auto;
  overflow: hidden;
}

.v5-server-drawer .q-expansion-item {
  margin: 2px 8px;
  overflow: hidden;
  border-radius: 14px;
}

.v5-server-drawer .q-item {
  min-height: 46px;
  border-radius: 12px;
}

.v5-server-drawer .active-library-item {
  border-left: 0 !important;
  border-radius: 12px !important;
  background: linear-gradient(90deg, rgba(255, 70, 106, 0.17), rgba(111, 77, 255, 0.08)) !important;
  animation: none !important;
  box-shadow: inset 0 0 0 1px rgba(255, 80, 113, 0.16) !important;
}

.v5-server-drawer .active-library-item::before {
  display: none !important;
}

.v5-drawer-footer {
  flex: 0 0 auto;
  padding: 5px 8px 9px;
  border-top: 1px solid var(--v5-border);
  background: rgba(0, 0, 0, 0.05);
}

.v5-drawer-section-header--footer {
  padding-left: 10px;
  padding-right: 10px;
}

.v5-drawer-footer .q-item {
  min-height: 44px;
  border-radius: 12px;
}

@media (max-width: 960px) {
  .v5-navigation-drawer .q-drawer__content {
    padding: 7px;
  }

  .v5-drawer-shell {
    border-radius: 20px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .v5-primary-nav__item {
    transition: none;
  }

  .v5-primary-nav__item:hover {
    transform: none;
  }
}
</style>
