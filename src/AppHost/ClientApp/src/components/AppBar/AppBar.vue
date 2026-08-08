<template>
	<q-header class="app-bar v5-app-bar">
		<q-toolbar class="v5-app-toolbar">
			<q-btn
				flat
				round
				dense
				:icon="showNavigationDrawerState ? 'mdi-menu-open' : 'mdi-menu'"
				class="v5-app-icon-button"
				aria-label="Toggle navigation"
				@click.stop="showNavigationDrawer" />

			<q-btn
				to="/"
				flat
				no-caps
				class="v5-app-brand q-ml-sm">
				<img
					src="/img/logo/reaparr-full.svg"
					alt="Reaparr"
					class="v5-app-brand__mark">
				<img
					src="/img/logo/reaparr-title.svg"
					alt="Reaparr"
					class="v5-app-brand__title">
			</q-btn>

			<button
				type="button"
				class="v5-version-chip"
				@click="copy(globalStore.version)">
				<q-icon name="mdi-source-branch" />
				<span>{{ globalStore.version }}</span>
				<q-tooltip>
					{{ $t('components.app-bar.copy-version', { version: globalStore.version }) }}
				</q-tooltip>
			</button>

			<IconButton
				v-if="updateStore.hasUpdateAvailable"
				icon="mdi-arrow-up-circle"
				class="v5-update-button"
				@click="openUpdateDialog">
				<q-tooltip>
					{{ $t('components.app-bar.update-available') }}
				</q-tooltip>
			</IconButton>

			<q-space />

			<div class="v5-app-actions">
				<ExternalLink
					href="https://github.com/Reaparr/Reaparr"
					class="v5-desktop-action">
					<IconButton
						icon="mdi-github"
						class="v5-app-action" />
				</ExternalLink>

				<IconButton
					class="v5-app-action v5-desktop-action"
					@click="dialogStore.openDialog(DialogType.DiscordServerInviteDialog)">
					<DiscordIcon />
				</IconButton>

				<BackgroundActivityToggleButton />
				<AccountSelector />
				<NotificationButton @toggle="showNotificationsDrawer" />
			</div>
		</q-toolbar>
	</q-header>
</template>

<script setup lang="ts">
import { useGlobalStore, useDialogStore, useUpdateStore } from '@store';
import { DialogType } from '@enums';
import { useClipboard } from '@vueuse/core';

const globalStore = useGlobalStore();
const dialogStore = useDialogStore();
const updateStore = useUpdateStore();

const { copy } = useClipboard({ legacy: true });

defineProps<{
	showNavigationDrawerState?: boolean;
}>();

const emit = defineEmits<{
	(e: 'show-navigation' | 'show-notifications'): void;
}>();

function showNavigationDrawer(): void {
	emit('show-navigation');
}

function showNotificationsDrawer(): void {
	emit('show-notifications');
}

function openUpdateDialog(): void {
	dialogStore.openDialog(DialogType.UpdateAvailableDialog);
}
</script>

<style lang="scss">
.v5-app-bar {
  background: transparent !important;
  box-shadow: none !important;
}

.v5-app-toolbar {
  min-height: 66px;
  margin: 10px 14px 0;
  padding: 0 12px;
  border: 1px solid var(--v5-border);
  border-radius: 20px;
  background: var(--v5-surface-strong) !important;
  box-shadow: var(--v5-shadow-md);
  backdrop-filter: blur(24px) saturate(145%);
}

.v5-app-icon-button,
.v5-app-action {
  color: var(--v5-text-muted);
  transition:
    color 160ms ease,
    background-color 160ms ease,
    transform 160ms ease;
}

.v5-app-icon-button:hover,
.v5-app-action:hover {
  color: var(--v5-text);
  background: var(--v5-surface-soft);
  transform: translateY(-1px);
}

.v5-app-brand {
  min-height: 46px;
  padding: 6px 10px;
  border-radius: 14px;
}

.v5-app-brand__mark {
  width: auto;
  height: 30px;
}

.v5-app-brand__title {
  width: auto;
  height: 22px;
  margin-left: 9px;
}

.v5-version-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  max-width: 180px;
  margin-left: 8px;
  padding: 5px 9px;
  overflow: hidden;
  border: 1px solid var(--v5-border);
  border-radius: 999px;
  background: var(--v5-surface-soft);
  color: var(--v5-text-muted);
  font: inherit;
  font-size: 0.68rem;
  cursor: pointer;
}

.v5-version-chip span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.v5-update-button {
  margin-left: 7px;
  color: #ff6b82;
  animation: v5-update-pulse 2.2s ease-in-out infinite;
}

.v5-app-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

@keyframes v5-update-pulse {
  0%,
  100% {
    filter: drop-shadow(0 0 0 rgba(255, 74, 108, 0));
  }

  50% {
    filter: drop-shadow(0 0 8px rgba(255, 74, 108, 0.5));
  }
}

@media (max-width: 760px) {
  .v5-app-toolbar {
    min-height: 58px;
    margin: 6px 7px 0;
    padding: 0 7px;
    border-radius: 16px;
  }

  .v5-app-brand {
    margin-left: 2px !important;
    padding-left: 5px;
    padding-right: 5px;
  }

  .v5-app-brand__mark {
    height: 26px;
  }

  .v5-app-brand__title,
  .v5-version-chip,
  .v5-desktop-action {
    display: none !important;
  }
}

@media (prefers-reduced-motion: reduce) {
  .v5-update-button {
    animation: none;
  }

  .v5-app-icon-button,
  .v5-app-action {
    transition: none;
  }
}
</style>
