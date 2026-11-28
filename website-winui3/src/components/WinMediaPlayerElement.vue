<template>
  <div
    ref="rootRef"
    class="win-media-player-element"
    :class="{ 'is-full-window': isFullWindowActive }"
    :style="rootStyle"
    @pointermove="showControls"
    @pointerleave="onRootExited"
    @pointerdown="onRootPressed"
    @pointerup="onRootReleased"
    @pointercancel="onRootReleased"
    @lostpointercapture="onRootCaptureLost">
    <div class="win-media-player-surface">
      <video
        ref="videoRef"
        class="win-media-player-video"
        :class="{ 'is-media-error': mediaError }"
        :poster="posterUri"
        :autoplay="AutoPlay"
        :crossorigin="CrossOrigin || undefined"
        preload="metadata"
        :style="videoStyle"
        playsinline
        @loadedmetadata="onMediaMetadata"
        @timeupdate="syncFromVideo"
        @durationchange="syncFromVideo"
        @loadeddata="onMediaLoaded"
        @error="onMediaError"
        @waiting="onBufferingStarted"
        @canplay="onBufferingEnded"
        @playing="onBufferingEnded"
        @play="onPlay"
        @pause="onPause"
        @ended="onEnded">
        <source v-if="sourceUri" :src="sourceUri" :type="sourceMimeType || undefined" />
      </video>

      <img v-if="mediaError && posterUri" class="win-media-player-poster-fallback" :src="posterUri" alt="" />

      <div
        v-if="AreTransportControlsEnabled"
        class="win-media-transport-controls"
        :class="{ visible: controlsVisible, 'is-media-loading': isMediaLoading || mediaError }">
        <div
          ref="controlPanelRef"
          class="win-media-transport-panel"
          @pointerenter="onControlPanelEntered"
          @pointerleave="onControlPanelExited"
          @pointerdown="onControlPanelPressed"
          @pointerup="onControlPanelReleased"
          @pointercancel="onControlPanelReleased"
          @lostpointercapture="onControlPanelCaptureLost"
          @focusin="onControlPanelFocusEntered"
          @focusout="onControlPanelFocusExited">
          <div v-if="mediaError" class="win-media-error" role="alert">
            {{ t('text.media-failed') }}
          </div>

          <div v-if="isSeekBarVisible" class="win-media-timeline-border">
            <div class="win-media-timeline-grid">
              <div class="win-media-progress-host">
                <div class="win-media-progress-slider">
                  <WinSlider
                    :Value="currentTime"
                    :Minimum="0"
                    :Maximum="duration || 1"
                    :SmallChange="1"
                    :StepFrequency="0.01"
                    :IsThumbToolTipEnabled="false"
                    Width="100%"
                    Height="32"
                    @update:Value="seekTo" />
                </div>
                <div v-if="isBuffering || mediaError" class="win-media-loading-progress">
                  <WinProgressBar
                    :IsIndeterminate="isBuffering || mediaError"
                    :ShowError="mediaError"
                    Width="100%"
                    Height="4" />
                </div>
              </div>
              <div class="win-media-time-text-grid">
                <span>{{ formatTime(currentTime) }}</span>
                <span>{{ formatTime(Math.max(0, duration - currentTime)) }}</span>
              </div>
            </div>
          </div>

          <div class="win-media-command-border">
            <div class="win-media-command-bar" role="toolbar" :aria-label="t('text.media-transport-controls')">
              <div class="win-media-command-left">
                <WinFlyout
                  v-if="isVolumeButtonVisible"
                  ref="volumeFlyoutRef"
                  v-model:IsOpen="isVolumeFlyoutOpen"
                  Placement="Top"
                  align="center"
                  :Theme="flyoutTheme">
                  <template #trigger>
                    <button
                      class="win-media-appbar-button"
                      type="button"
                      :aria-label="t('text.volume')"
                      v-bind="{ 'tooltipservice.tooltip': t('text.volume') }"
                      @click.stop="toggleVolumeFlyout">
                      <span class="win-media-glyph" aria-hidden="true">{{ volumeGlyph }}</span>
                    </button>
                  </template>
                  <div class="win-media-volume-panel">
                    <button
                      class="win-media-appbar-button"
                      type="button"
                      :aria-label="muted ? t('text.unmute') : t('text.mute')"
                      v-bind="{ 'tooltipservice.tooltip': muted ? t('text.unmute') : t('text.mute') }"
                      @click="toggleMute">
                      <span class="win-media-glyph" aria-hidden="true">{{ volumeGlyph }}</span>
                    </button>
                    <div class="win-media-volume-slider">
                      <WinSlider
                        :Value="volumePercent"
                        :Minimum="0"
                        :Maximum="100"
                        :SmallChange="1"
                        :StepFrequency="1"
                        :IsThumbToolTipEnabled="false"
                        Width="190"
                        Height="32"
                        @update:Value="setVolume" />
                    </div>
                    <span class="win-media-volume-value">{{ Math.round(volumePercent) }}</span>
                  </div>
                </WinFlyout>
              </div>

              <div class="win-media-command-center">
                <button
                  class="win-media-appbar-button"
                  type="button"
                  :aria-label="isPlaying ? t('text.pause') : t('text.play')"
                  v-bind="{ 'tooltipservice.tooltip': isPlaying ? t('text.pause') : t('text.play') }"
                  @click="togglePlay">
                  <span class="win-media-glyph" aria-hidden="true">{{ playGlyph }}</span>
                </button>
              </div>

              <div class="win-media-command-right">
                <button
                  v-if="isZoomButtonVisible"
                  class="win-media-appbar-button"
                  type="button"
                  :aria-label="t('text.aspect-ratio')"
                  v-bind="{ 'tooltipservice.tooltip': t('text.aspect-ratio') }"
                  @click="toggleStretch">
                  <span class="win-media-glyph" aria-hidden="true">&#xE799;</span>
                </button>

                <button
                  v-if="isCastButtonVisible"
                  class="win-media-appbar-button"
                  type="button"
                  :aria-label="t('text.cast')"
                  v-bind="{ 'tooltipservice.tooltip': t('text.cast') }"
                  @click="onCastRequested">
                  <span class="win-media-glyph" aria-hidden="true">&#xEC15;</span>
                </button>

                <button
                  v-if="isFullWindowButtonVisible"
                  class="win-media-appbar-button"
                  type="button"
                  :aria-label="isFullWindowActive ? t('text.exit-full-screen') : t('text.full-screen')"
                  v-bind="{ 'tooltipservice.tooltip': isFullWindowActive ? t('text.exit-full-screen') : t('text.full-screen') }"
                  @click="toggleFullWindow">
                  <span class="win-media-glyph" aria-hidden="true">{{ isFullWindowActive ? '\uE73F' : '\uE740' }}</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, inject, nextTick, onBeforeUnmount, onMounted, ref, unref, watch } from 'vue';
import WinFlyout from './WinFlyout.vue';
import WinProgressBar from './WinProgressBar.vue';
import WinSlider from './WinSlider.vue';
import { useI18n } from './i18n/index';

const props = defineProps({
  Source: { type: [String, Object], default: '' },
  AreTransportControlsEnabled: { type: Boolean, default: false },
  CrossOrigin: { type: String, default: '' },
  PosterSource: { type: [String, Object], default: '' },
  Stretch: { type: String, default: 'Uniform' },
  AutoPlay: { type: Boolean, default: false },
  IsFullWindow: { type: Boolean, default: false },
  TransportControls: { type: Object, default: null },
  MaxWidth: { type: [String, Number], default: '' },
  Width: { type: [String, Number], default: '' },
  Height: { type: [String, Number], default: '' },
  MinWidth: { type: [String, Number], default: '' },
  MinHeight: { type: [String, Number], default: '' },
  MaxHeight: { type: [String, Number], default: '' },
  HorizontalAlignment: { type: String, default: '' },
  VerticalAlignment: { type: String, default: '' }
});

const emit = defineEmits(['MediaOpened', 'MediaFailed', 'IsFullWindowChanged']);
const { t } = useI18n();
const inheritedTheme = inject('winuiTheme', null);
const rootRef = ref<HTMLElement | null>(null);
const videoRef = ref<HTMLVideoElement | null>(null);
const volumeFlyoutRef = ref<{ toggle?: () => void } | null>(null);
const controlPanelRef = ref<HTMLElement | null>(null);
const activeStretch = ref(props.Stretch);
const isPlaying = ref(false);
const muted = ref(false);
const currentTime = ref(0);
const duration = ref(0);
const volumePercent = ref(50);
const mediaError = ref(false);
const isBuffering = ref(false);
const isMediaLoading = ref(false);
const isVolumeFlyoutOpen = ref(false);
const fullWindowState = ref(false);
const controlsVisible = ref(true);
const controlPanelPointerOver = ref(false);
const controlPanelPointerPressed = ref(false);
const controlPanelHasFocus = ref(false);
const rootPointerPressed = ref(false);
const anchorTheme = ref('');
let hideControlsTimer: number | null = null;
let pointerMoveEndTimer: number | null = null;
let themeObserver: MutationObserver | null = null;
let mediaLoadTimer: number | null = null;
let remotePlayback: RemotePlayback | null = null;

const cssLength = (value: unknown) => {
  if (value === '' || value === null || value === undefined) return undefined;
  return typeof value === 'number' || /^-?\d+(\.\d+)?$/.test(String(value).trim())
    ? `${value}px`
    : String(value);
};

const mediaUri = (value: unknown) => {
  if (typeof value === 'string') return value;
  if (value && typeof value === 'object') return String((value as { UriSource?: unknown }).UriSource || '');
  return '';
};

const sourceUri = computed(() => mediaUri(props.Source));
const posterUri = computed(() => mediaUri(props.PosterSource));
const sourceMimeType = computed(() => {
  const path = sourceUri.value.split(/[?#]/, 1)[0].toLowerCase();
  if (path.endsWith('.mp4') || path.endsWith('.m4v')) return 'video/mp4';
  if (path.endsWith('.webm')) return 'video/webm';
  if (path.endsWith('.ogv') || path.endsWith('.ogg')) return 'video/ogg';
  return '';
});
const resolveAnchorTheme = () => {
  const scope = rootRef.value?.closest?.('.theme-light, .theme-dark');
  if (scope?.classList.contains('theme-dark')) return 'dark';
  if (scope?.classList.contains('theme-light')) return 'light';
  return '';
};
const flyoutTheme = computed(() => {
  const localTheme = String(anchorTheme.value || '').toLowerCase();
  if (localTheme === 'light' || localTheme === 'dark') return localTheme;
  const providedTheme = String(unref(inheritedTheme) || '').toLowerCase();
  if (providedTheme === 'light' || providedTheme === 'dark') return providedTheme;
  if (typeof document !== 'undefined') {
    const root = document.documentElement;
    if (root.classList.contains('theme-dark') || root.dataset.theme === 'dark') return 'dark';
    if (root.classList.contains('theme-light') || root.dataset.theme === 'light') return 'light';
  }
  return '';
});
const transport = computed(() => props.TransportControls || {});
const isSeekBarVisible = computed(() => transport.value.IsSeekBarVisible !== false);
const isVolumeButtonVisible = computed(() => transport.value.IsVolumeButtonVisible !== false);
const isZoomButtonVisible = computed(() => transport.value.IsZoomButtonVisible !== false);
const isCastButtonVisible = computed(() => transport.value.IsCastButtonVisible !== false);
const isFullWindowButtonVisible = computed(() => transport.value.IsFullWindowButtonVisible !== false);
const showAndHideAutomatically = computed(() => transport.value.ShowAndHideAutomatically !== false);
const stretchValue = computed(() => ({
  None: 'none',
  Fill: 'fill',
  Uniform: 'contain',
  UniformToFill: 'cover'
}[activeStretch.value] || 'contain'));
const videoStyle = computed(() => ({ objectFit: stretchValue.value as 'none' | 'fill' | 'contain' | 'cover' }));
const isFullWindowActive = computed(() => fullWindowState.value);
const rootStyle = computed(() => ({
  width: cssLength(props.Width),
  height: cssLength(props.Height),
  minWidth: cssLength(props.MinWidth),
  minHeight: cssLength(props.MinHeight),
  maxWidth: isFullWindowActive.value ? undefined : cssLength(props.MaxWidth),
  maxHeight: cssLength(props.MaxHeight),
  justifySelf: ({ Left: 'start', Center: 'center', Right: 'end', Stretch: 'stretch' })[props.HorizontalAlignment] || undefined,
  alignSelf: ({ Top: 'start', Center: 'center', Bottom: 'end', Stretch: 'stretch' })[props.VerticalAlignment] || undefined
}));
const playGlyph = computed(() => isPlaying.value ? '\uF8AE' : '\uF5B0');
const volumeGlyph = computed(() => muted.value || volumePercent.value === 0 ? '\uE74F' : '\uE767');

const syncFromVideo = () => {
  const video = videoRef.value;
  if (!video) return;
  currentTime.value = video.currentTime || 0;
  duration.value = Number.isFinite(video.duration) ? video.duration : 0;
  muted.value = video.muted;
  volumePercent.value = Math.round((video.volume || 0) * 100);
  if (video.readyState >= 1) emit('MediaOpened', video);
};

const clearMediaLoadTimer = () => {
  if (mediaLoadTimer) window.clearTimeout(mediaLoadTimer);
  mediaLoadTimer = null;
};

const armMediaLoadTimer = () => {
  clearMediaLoadTimer();
  if (!sourceUri.value) return;
  // Chromium/WebView2 does not report a decode failure for every unsupported
  // container immediately (WMV is a common example). Match WinUI's Error
  // state instead of leaving the transport controls in Loading forever.
  mediaLoadTimer = window.setTimeout(() => {
    if (isMediaLoading.value && !mediaError.value) onMediaError({ type: 'error', target: videoRef.value });
  }, 12000);
};

const onMediaLoaded = () => {
  clearMediaLoadTimer();
  mediaError.value = false;
  isBuffering.value = false;
  isMediaLoading.value = false;
  syncFromVideo();
};

// preload="metadata" 只加载元数据，不会触发 loadeddata；元数据到手就说明容器可解码，
// 必须在这里清掉 12 秒的加载看门狗，否则会误报「媒体加载失败」。
const onMediaMetadata = () => {
  clearMediaLoadTimer();
  mediaError.value = false;
  syncFromVideo();
};

const onBufferingStarted = () => {
  isBuffering.value = true;
  const video = videoRef.value;
  if (video && video.readyState >= 2) isMediaLoading.value = false;
  showControls();
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
};

const onBufferingEnded = () => {
  clearMediaLoadTimer();
  isBuffering.value = false;
  isMediaLoading.value = false;
  startControlPanelHideTimer();
};

const onMediaError = (event: Event | { type: string; target: HTMLVideoElement | null }) => {
  clearMediaLoadTimer();
  mediaError.value = true;
  isBuffering.value = false;
  isMediaLoading.value = false;
  showControls();
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
  hideControlsTimer = null;
  emit('MediaFailed', event);
};

const onPlay = () => {
  isPlaying.value = true;
  showControls();
};

const onPause = () => {
  isPlaying.value = false;
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
  hideControlsTimer = null;
  controlsVisible.value = true;
};

const togglePlay = () => {
  const video = videoRef.value;
  if (!video) return;
  if (video.paused) video.play().catch(() => {});
  else video.pause();
};

const toggleVolumeFlyout = () => volumeFlyoutRef.value?.toggle?.();

const toggleMute = () => {
  const video = videoRef.value;
  if (!video) return;
  video.muted = !video.muted;
  muted.value = video.muted;
  if (!video.muted && video.volume === 0) {
    video.volume = 1;
    volumePercent.value = 100;
  }
};

const setVolume = (value: unknown) => {
  const video = videoRef.value;
  if (!video) return;
  const nextValue = Math.max(0, Math.min(100, Number(
    value && typeof value === 'object' && 'NewValue' in value ? value.NewValue : value
  )));
  video.volume = nextValue / 100;
  video.muted = nextValue === 0;
  volumePercent.value = nextValue;
  muted.value = video.muted;
};

const isRootFullscreen = () => {
  const root = rootRef.value;
  return Boolean(root && (document.fullscreenElement === root || root.matches(':fullscreen')));
};

const syncFullscreenState = () => {
  const nextState = isRootFullscreen();
  fullWindowState.value = nextState;
  if (!nextState) isVolumeFlyoutOpen.value = false;
  emit('IsFullWindowChanged', nextState);
};

const enterFullWindow = async () => {
  const root = rootRef.value;
  if (!root?.requestFullscreen) return;
  try {
    await root.requestFullscreen();
  } catch {
    fullWindowState.value = false;
  } finally {
    syncFullscreenState();
  }
};

const exitFullWindow = async () => {
  try {
    if (document.fullscreenElement && document.exitFullscreen) await document.exitFullscreen();
  } catch {
    // The browser may already have left fullscreen (for example through Esc).
  } finally {
    fullWindowState.value = false;
    syncFullscreenState();
  }
};

const toggleFullWindow = () => {
  if (isRootFullscreen()) void exitFullWindow();
  else void enterFullWindow();
};

const onFullscreenChanged = () => syncFullscreenState();

const seekTo = (value: unknown) => {
  const video = videoRef.value;
  if (!video) return;
  video.currentTime = Number(value && typeof value === 'object' && 'NewValue' in value ? value.NewValue : value);
  syncFromVideo();
};

const toggleStretch = () => {
  const values = ['Uniform', 'Fill', 'UniformToFill', 'None'];
  activeStretch.value = values[(values.indexOf(activeStretch.value) + 1) % values.length];
};

const postCastRequestToWebView = () => {
  const hostWindow = window as Window & {
    chrome?: { webview?: { postMessage?: (message: unknown) => void } };
  };
  const webview = hostWindow.chrome?.webview;
  if (!webview?.postMessage) return false;
  webview.postMessage({
    source: 'WinUIonWeb',
    type: 'mediaCastRequested',
    sourceUri: videoRef.value?.currentSrc || sourceUri.value
  });
  return true;
};

const onCastRequested = async () => {
  const video = videoRef.value;
  const remote = video?.remote;
  if (remote?.prompt) {
    try {
      await remote.prompt();
      return;
    } catch {
      // Fall through to the WebView2 host bridge when native casting is unavailable.
    }
  }
  postCastRequestToWebView();
};

const attachRemotePlayback = () => {
  const video = videoRef.value;
  if (!video || !('remote' in video)) return;
  remotePlayback = video.remote;
  remotePlayback.onconnecting = () => showControls();
  remotePlayback.onconnect = () => showControls();
  remotePlayback.ondisconnect = () => showControls();
};

const detachRemotePlayback = () => {
  if (!remotePlayback) return;
  remotePlayback.onconnecting = null;
  remotePlayback.onconnect = null;
  remotePlayback.ondisconnect = null;
  remotePlayback = null;
};

const showControls = () => {
  controlsVisible.value = true;
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
  if (pointerMoveEndTimer) window.clearTimeout(pointerMoveEndTimer);
  pointerMoveEndTimer = window.setTimeout(() => {
    pointerMoveEndTimer = null;
    startControlPanelHideTimer();
  }, 0);
};

const startControlPanelHideTimer = () => {
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
  hideControlsTimer = null;
  if (!showAndHideAutomatically.value || !isPlaying.value || isBuffering.value || mediaError.value || controlPanelPointerOver.value || controlPanelPointerPressed.value || controlPanelHasFocus.value || rootPointerPressed.value || isVolumeFlyoutOpen.value) return;
  hideControlsTimer = window.setTimeout(() => {
    controlsVisible.value = false;
    hideControlsTimer = null;
  }, 3000);
};

const onControlPanelEntered = () => {
  controlPanelPointerOver.value = true;
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
};

const onControlPanelExited = () => {
  controlPanelPointerOver.value = false;
  startControlPanelHideTimer();
};

const onControlPanelPressed = () => {
  controlPanelPointerPressed.value = true;
  controlPanelHasFocus.value = false;
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
};

const onControlPanelReleased = () => {
  controlPanelPointerPressed.value = false;
  startControlPanelHideTimer();
};

const onControlPanelCaptureLost = (event: PointerEvent) => {
  rootPointerPressed.value = false;
  controlPanelPointerPressed.value = false;
  const point = event && Number.isFinite(event.clientX) && Number.isFinite(event.clientY) ? event : null;
  const hit = point ? document.elementFromPoint(point.clientX, point.clientY) : null;
  controlPanelPointerOver.value = Boolean(hit && controlPanelRef.value?.contains(hit));
  if (!controlPanelPointerOver.value) startControlPanelHideTimer();
};

const onControlPanelFocusEntered = () => {
  if (controlPanelPointerPressed.value) return;
  controlPanelHasFocus.value = true;
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
};

const onControlPanelFocusExited = (event: FocusEvent) => {
  const nextTarget = event.relatedTarget;
  if (nextTarget instanceof Node && (event.currentTarget as HTMLElement).contains(nextTarget)) return;
  controlPanelHasFocus.value = false;
  startControlPanelHideTimer();
};

const onRootPressed = () => {
  rootPointerPressed.value = true;
  controlPanelHasFocus.value = false;
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
  showControls();
};

const onRootReleased = () => {
  rootPointerPressed.value = false;
  startControlPanelHideTimer();
};

const onRootCaptureLost = () => {
  rootPointerPressed.value = false;
  startControlPanelHideTimer();
};

const onRootExited = () => {
  rootPointerPressed.value = false;
  controlPanelPointerOver.value = false;
  startControlPanelHideTimer();
};

const onEnded = () => {
  isPlaying.value = false;
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
  hideControlsTimer = null;
  controlsVisible.value = true;
};

const formatTime = (value: unknown) => {
  const safe = Math.max(0, Math.floor(Number(value) || 0));
  const hours = Math.floor(safe / 3600);
  const minutes = Math.floor((safe % 3600) / 60);
  const seconds = String(safe % 60).padStart(2, '0');
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`;
};

watch(sourceUri, async () => {
  clearMediaLoadTimer();
  mediaError.value = false;
  isBuffering.value = true;
  isMediaLoading.value = Boolean(sourceUri.value);
  showControls();
  armMediaLoadTimer();
  await nextTick();
  if (videoRef.value) {
    videoRef.value.load();
    if (props.AutoPlay) videoRef.value.play().catch(() => {});
  }
});

watch(() => props.AutoPlay, (value: boolean) => {
  if (value) videoRef.value?.play().catch(() => {});
  else videoRef.value?.pause();
});

watch(() => props.IsFullWindow, (value: boolean) => {
  if (value && !isRootFullscreen()) void enterFullWindow();
  else if (!value && isRootFullscreen()) void exitFullWindow();
});

watch(isVolumeFlyoutOpen, (isOpen) => {
  if (isOpen) showControls();
  else startControlPanelHideTimer();
});

watch(showAndHideAutomatically, (enabled) => {
  if (!enabled) {
    if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
    hideControlsTimer = null;
    controlsVisible.value = true;
  } else {
    startControlPanelHideTimer();
  }
});

watch(() => props.Stretch, (value: string) => {
  activeStretch.value = value;
});

onMounted(() => {
  document.addEventListener('fullscreenchange', onFullscreenChanged);
  document.addEventListener('webkitfullscreenchange', onFullscreenChanged as EventListener);
  anchorTheme.value = resolveAnchorTheme();
  themeObserver = new MutationObserver(() => {
    anchorTheme.value = resolveAnchorTheme();
  });
  const themeScope = rootRef.value?.closest?.('.theme-light, .theme-dark');
  if (themeScope) themeObserver.observe(themeScope, { attributes: true, attributeFilter: ['class'] });
  if (document.documentElement !== themeScope) {
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'data-theme'] });
  }
  if (videoRef.value) {
    isMediaLoading.value = Boolean(sourceUri.value);
    isBuffering.value = Boolean(sourceUri.value);
    armMediaLoadTimer();
    videoRef.value.load();
    videoRef.value.muted = false;
    videoRef.value.volume = 0.5;
    attachRemotePlayback();
    if (props.AutoPlay) videoRef.value.play().catch(() => {});
  }
  if (props.IsFullWindow) void enterFullWindow();
});

onBeforeUnmount(() => {
  document.removeEventListener('fullscreenchange', onFullscreenChanged);
  document.removeEventListener('webkitfullscreenchange', onFullscreenChanged as EventListener);
  detachRemotePlayback();
  themeObserver?.disconnect();
  if (hideControlsTimer) window.clearTimeout(hideControlsTimer);
  if (pointerMoveEndTimer) window.clearTimeout(pointerMoveEndTimer);
  clearMediaLoadTimer();
});

defineExpose({ MediaPlayer: videoRef });
</script>

<style>
@font-face {
  font-family: 'WinUIOnWebIcons';
  src: url('../assets/Fonts/SEGOEICONS.TTF') format('truetype');
  font-display: block;
}

.win-media-player-element {
  position: relative;
  display: block;
  width: 100%;
  overflow: visible;
  box-sizing: border-box;
}

.win-media-player-element:fullscreen {
  position: fixed;
  inset: 0;
  z-index: 2147483647;
  display: block;
  width: 100vw !important;
  height: 100vh !important;
  max-width: none !important;
  max-height: none !important;
  margin: 0 !important;
  background: #000;
}

.win-media-player-surface {
  position: relative;
  width: 100%;
  overflow: hidden;
  background: #000;
  border: 1px solid var(--card-stroke);
  box-sizing: border-box;
}

.win-media-player-element:fullscreen .win-media-player-surface {
  width: 100%;
  height: 100%;
  border: 0;
}

.win-media-player-video {
  display: block;
  width: 100%;
  min-height: 120px;
  aspect-ratio: 16 / 9;
  background: #000;
}

.win-media-player-element:fullscreen .win-media-player-video {
  width: 100%;
  height: 100%;
  min-height: 0;
  aspect-ratio: auto;
}

.win-media-player-video.is-media-error { opacity: 0; }

.win-media-player-poster-fallback {
  position: absolute;
  inset: 0;
  z-index: 1;
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
  background: #000;
}

.win-media-transport-controls {
  position: absolute;
  z-index: 2;
  inset: 0;
  display: flex;
  align-items: flex-end;
  justify-content: center;
  pointer-events: none;
  opacity: 0;
  transition: opacity 300ms cubic-bezier(.1, .9, .2, 1);
}

.win-media-transport-controls.visible {
  pointer-events: auto;
  opacity: 1;
}

.win-media-transport-panel {
  position: relative;
  isolation: isolate;
  width: min(720px, calc(100% - 24px));
  min-width: min(296px, calc(100% - 24px));
  margin: 0 12px 12px;
  overflow: hidden;
  color: var(--MediaTransportControlsFillMediaText, var(--text-primary, currentColor));
  background: transparent;
  border: 1px solid var(--MediaTransportControlsBorderBrush, var(--surface-stroke-color-flyout, var(--stroke-surface-flyout, transparent)));
  border-radius: var(--overlay-corner-radius, 8px);
  box-sizing: border-box;
  -webkit-backdrop-filter: blur(30px);
  backdrop-filter: blur(30px);
  transform: translateY(50px);
  transition: transform 300ms cubic-bezier(.1, .9, .2, 1);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  grid-template-rows: auto auto auto;
}

.win-media-transport-panel::before {
  position: absolute;
  inset: 0;
  z-index: -1;
  pointer-events: none;
  content: '';
  background: var(--MediaTransportControlsPanelBackground, var(--flyout-bg, var(--layer-default, transparent)));
  border-radius: inherit;
}

.win-media-transport-controls.visible .win-media-transport-panel { transform: translateY(.5px); }

.win-media-error {
  grid-column: 1 / -1;
  grid-row: 1;
  width: 320px;
  height: 96px;
  justify-self: center;
  align-self: center;
  box-sizing: border-box;
  padding: 12px;
  font-size: 12px;
  line-height: 16px;
  text-align: center;
}

.win-media-timeline-border {
  grid-column: 2;
  grid-row: 2;
  display: block;
  width: 100%;
  box-sizing: border-box;
}

.win-media-timeline-grid {
  display: grid;
  grid-template-rows: 35px 16px;
  width: 100%;
}

.win-media-progress-host {
  position: relative;
  width: 100%;
  height: 35px;
  padding: 0;
  box-sizing: border-box;
}

.win-media-progress-slider {
  display: block;
  width: auto;
  height: 32px;
  margin: 2px 7px 1px;
  box-sizing: border-box;
}

.win-media-progress-slider > .win-slider-root { display: block; width: 100%; }
.win-media-progress-slider .win-slider { width: 100% !important; height: 32px !important; }

.win-media-loading-progress {
  position: absolute;
  top: 2px;
  left: 0;
  width: 100%;
  height: 4px;
  pointer-events: none;
}

.win-media-loading-progress > .win-progress-bar {
  display: block;
  width: 100%;
  height: 4px;
}

.win-media-time-text-grid {
  display: flex;
  justify-content: space-between;
  height: 16px;
  margin: 0 7px 2px;
  color: var(--MediaTransportControlsFillTimeElapsedText, var(--text-secondary, currentColor));
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  line-height: 16px;
}

.win-media-command-border {
  grid-column: 2;
  grid-row: 3;
  width: 100%;
  box-sizing: border-box;
}

.win-media-command-bar {
  position: relative;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  min-height: 40px;
  margin: 0 0 3px;
  padding: 0;
  box-sizing: border-box;
}

.win-media-transport-controls.is-media-loading .win-media-progress-slider,
.win-media-transport-controls.is-media-loading .win-media-command-bar {
  opacity: 0;
  pointer-events: none;
}

.win-media-command-left,
.win-media-command-center,
.win-media-command-right {
  display: flex;
  align-items: center;
  min-width: 0;
}

.win-media-command-center {
  position: absolute;
  left: 50%;
  justify-content: center;
  transform: translateX(-50%);
}
.win-media-command-right { justify-content: flex-end; }

.win-media-appbar-button {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 40px;
  width: 40px;
  height: 40px;
  min-width: 40px;
  min-height: 40px;
  margin: 0;
  padding: 0;
  color: inherit;
  background: transparent;
  border: 0;
  border-radius: 4px;
  box-sizing: border-box;
  cursor: pointer;
}

.win-media-appbar-button::before {
  position: absolute;
  inset: 5px;
  content: '';
  pointer-events: none;
  background: transparent;
  border-radius: 4px;
}

.win-media-appbar-button:hover::before { background: var(--subtle-secondary, transparent); }
.win-media-appbar-button:active::before { background: var(--subtle-tertiary, transparent); }
.win-media-appbar-button:focus-visible { outline: 2px solid currentColor; outline-offset: -2px; }

.win-media-glyph {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 16px;
  font-family: 'WinUIOnWebIcons';
  font-size: 16px;
  font-weight: 400;
  line-height: 16px;
}

.win-media-volume-panel {
  display: flex;
  align-items: center;
  height: 62px;
  padding: 11px 3px;
  box-sizing: border-box;
  background: transparent;
}

.win-media-volume-slider {
  display: block;
  flex: 0 0 190px;
  width: 190px;
  height: 32px;
  margin: 0 8px 0 12px;
}

.win-media-volume-slider > .win-slider-root { display: block; width: 190px; }
.win-media-volume-slider .win-slider { width: 190px !important; height: 32px !important; }

.win-media-volume-value {
  display: inline-flex;
  align-items: center;
  justify-content: flex-end;
  width: 24px;
  height: 20px;
  margin: 0 16px 0 8px;
  color: var(--MediaTransportControlsFillMediaText, var(--text-primary, currentColor));
  font-size: 12px;
  line-height: 20px;
  text-align: right;
}

.win-flyout:has(.win-media-volume-panel) {
  min-width: 0;
  padding: 0;
  color: var(--text-primary);
  background: transparent;
  border-radius: var(--overlay-corner-radius, 8px);
}

.win-flyout:has(.win-media-volume-panel)::before {
  position: absolute;
  inset: 0;
  z-index: -1;
  pointer-events: none;
  content: '';
  background: var(--MediaTransportControlsFlyoutBackground, var(--flyout-bg, var(--layer-default, transparent)));
  border-radius: inherit;
}

.win-flyout:has(.win-media-volume-panel) .win-flyout-scroll { max-height: none; }

</style>
