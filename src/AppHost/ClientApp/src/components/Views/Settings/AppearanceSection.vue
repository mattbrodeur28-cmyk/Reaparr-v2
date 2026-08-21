<template>
	<QSection :header="$t('pages.settings.ui.appearance-settings.header')">
		<HelpGroup class="q-mt-md">
			<!--	Application Theme	-->
			<HelpRow
				:label="$t('help.settings.ui.appearance-settings.theme.label')"
				:title="$t('help.settings.ui.appearance-settings.theme.title')"
				:text="$t('help.settings.ui.appearance-settings.theme.text')">
				<div class="column items-end">
					<q-option-group
						:model-value="themeStore.theme"
						:options="themeOptions"
						color="primary"
						inline
						data-cy="theme-selector"
						@update:model-value="themeStore.setTheme($event)" />
					<!-- Describes whichever theme is selected, so the effect of the choice is
						visible without switching to find out. -->
					<div class="text-caption text-grey q-mt-xs text-right">
						{{ activeThemeCaption }}
					</div>
				</div>
			</HelpRow>
		</HelpGroup>
	</QSection>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useThemeStore } from '@store';
import type { ReaparrTheme } from '@store';

const themeStore = useThemeStore();
const { t } = useI18n();

const themeOptions = computed<{ label: string; value: ReaparrTheme }[]>(() => [
	{ label: t('pages.settings.ui.appearance-settings.theme.modern'), value: 'modern' },
	{ label: t('pages.settings.ui.appearance-settings.theme.classic'), value: 'classic' },
]);

const activeThemeCaption = computed(() =>
	themeStore.isModern
		? t('pages.settings.ui.appearance-settings.theme.modern-caption')
		: t('pages.settings.ui.appearance-settings.theme.classic-caption'),
);
</script>
