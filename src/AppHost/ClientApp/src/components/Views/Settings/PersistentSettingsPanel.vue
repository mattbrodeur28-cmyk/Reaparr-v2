<template>
	<q-expansion-item
		v-model="expanded"
		class="persistent-settings-panel"
		:icon="icon"
		:label="title"
		:caption="subtitle"
		expand-separator
		header-class="persistent-settings-panel__header">
		<div class="persistent-settings-panel__content">
			<slot />
		</div>
	</q-expansion-item>
</template>

<script setup lang="ts">
import { useLocalStorage } from '@vueuse/core';

const props = withDefaults(defineProps<{
	storageKey: string;
	title: string;
	subtitle?: string;
	icon?: string;
	defaultOpen?: boolean;
}>(), {
	subtitle: '',
	icon: 'mdi-tune-variant',
	defaultOpen: false,
});

const expanded = useLocalStorage<boolean>(
	props.storageKey,
	props.defaultOpen,
);
</script>

<style lang="scss">
.persistent-settings-panel {
  overflow: hidden;
  border: 1px solid var(--v5-border);
  border-radius: 23px;
  background: var(--v5-surface);
  box-shadow: var(--v5-shadow-sm);
  backdrop-filter: blur(18px);
  -webkit-backdrop-filter: blur(18px);
}

.persistent-settings-panel__header {
  min-height: 68px;
  padding: 10px 16px;
}

.persistent-settings-panel__content {
  border-top: 1px solid var(--v5-border);
}

.persistent-settings-panel__content > .q-row {
  background: transparent;
}

@media (max-width: 700px) {
  .persistent-settings-panel {
    border-radius: 18px;
  }

  .persistent-settings-panel__header {
    min-height: 60px;
    padding-right: 11px;
    padding-left: 11px;
  }
}
</style>
