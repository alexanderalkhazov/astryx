import { BottomSheetBackdrop, type BottomSheetBackdropProps, BottomSheetModal, BottomSheetView } from '@gorhom/bottom-sheet';
import { color, motion, radius, space } from '@astryx/design-tokens';
import { type ReactNode, useCallback, useEffect, useRef } from 'react';
import { BackHandler, StyleSheet, useWindowDimensions } from 'react-native';
import { ReduceMotion, useReducedMotion } from 'react-native-reanimated';

export interface SheetProps {
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  /**
   * False while something must not be interrupted, e.g. an order is sending (design: "drag down or tap the backdrop
   * to dismiss, except while an order is sending"). Disables pan-to-close, backdrop taps and Android back.
   */
  dismissible?: boolean;
  accessibilityLabel: string;
}

/** Interaction rules for a sheet; exported for tests. */
export function sheetBehaviour(dismissible: boolean) {
  return {
    enablePanDownToClose: dismissible,
    backdropPressBehavior: dismissible ? ('close' as const) : ('none' as const),
    /** Android back: dismiss when allowed; otherwise swallow it so the screen behind doesn't pop either. */
    onHardwareBack: (dismiss: () => void) => {
      if (dismissible) dismiss();
      return true;
    },
  };
}

/** Bottom sheet (design 03 · Sheet): snaps to content height up to 88 % of the screen, grab handle, fading backdrop. */
export function Sheet({ open, onClose, children, dismissible = true, accessibilityLabel }: SheetProps) {
  const ref = useRef<BottomSheetModal>(null);
  const { height } = useWindowDimensions();
  const reduceMotion = useReducedMotion();
  const behaviour = sheetBehaviour(dismissible);

  // Whether the modal is up. Dismissing a modal that was never presented leaves bottom-sheet unable to present it
  // later, so a sheet that mounts closed must not call dismiss().
  const presented = useRef(false);

  useEffect(() => {
    if (!open && !presented.current) return undefined;
    // Defer one frame: presenting in the same commit that mounts the modal is ignored.
    const frame = requestAnimationFrame(() => {
      if (open) {
        presented.current = true;
        ref.current?.present();
      } else {
        presented.current = false;
        ref.current?.dismiss();
      }
    });
    return () => cancelAnimationFrame(frame);
  }, [open]);

  const handleDismiss = useCallback(() => {
    presented.current = false;
    onClose();
  }, [onClose]);

  useEffect(() => {
    if (!open) return undefined;
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => behaviour.onHardwareBack(() => ref.current?.dismiss()));
    return () => subscription.remove();
  }, [open, behaviour]);

  const renderBackdrop = useCallback(
    (props: BottomSheetBackdropProps) => (
      <BottomSheetBackdrop {...props} appearsOnIndex={0} disappearsOnIndex={-1} opacity={0.6} pressBehavior={behaviour.backdropPressBehavior} />
    ),
    [behaviour.backdropPressBehavior],
  );

  return (
    <BottomSheetModal
      ref={ref}
      enableDynamicSizing
      maxDynamicContentSize={Math.round(height * 0.88)}
      enablePanDownToClose={behaviour.enablePanDownToClose}
      onDismiss={handleDismiss}
      backdropComponent={renderBackdrop}
      backgroundStyle={styles.background}
      handleIndicatorStyle={styles.handle}
      animationConfigs={{ duration: reduceMotion ? 0 : motion.sheet, reduceMotion: ReduceMotion.System }}
      style={styles.shadow}
      // bottom-sheet makes the container one accessible element by default, hiding every control inside from VoiceOver.
      accessible={false}
    >
      <BottomSheetView style={styles.content} accessibilityViewIsModal accessibilityLabel={accessibilityLabel}>
        {children}
      </BottomSheetView>
    </BottomSheetModal>
  );
}

const styles = StyleSheet.create({
  background: { backgroundColor: color.surface2, borderTopLeftRadius: radius.sheet, borderTopRightRadius: radius.sheet },
  handle: { backgroundColor: color.lineStrong, width: 36 },
  content: { paddingHorizontal: space[4], paddingBottom: space[6], gap: space[3] },
  shadow: { shadowColor: '#000', shadowOpacity: 0.55, shadowRadius: 30, shadowOffset: { width: 0, height: -12 } },
});
