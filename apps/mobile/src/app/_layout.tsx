// Per-weight imports: the package root would bundle all 36 faces.
import { Geist_400Regular } from '@expo-google-fonts/geist/400Regular';
import { Geist_500Medium } from '@expo-google-fonts/geist/500Medium';
import { Geist_600SemiBold } from '@expo-google-fonts/geist/600SemiBold';
import { GeistMono_500Medium } from '@expo-google-fonts/geist-mono/500Medium';
import { GeistMono_600SemiBold } from '@expo-google-fonts/geist-mono/600SemiBold';
import { BottomSheetModalProvider } from '@gorhom/bottom-sheet';
import { color } from '@astryx/design-tokens';
import { QueryClientProvider } from '@tanstack/react-query';
import { useFonts } from 'expo-font';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect } from 'react';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { ToastHost } from '@/components/ToastHost';
import { queryClient } from '@/lib/queryClient';
import { restoreSession } from '@/lib/session';
import { useSession } from '@/state/session';

void SplashScreen.preventAutoHideAsync();

// Root: providers, auth gate (T-034), sheet portal and toast host (T-032), SignalR lifecycle (SYSTEM_DESIGN.md §8.3).
export default function RootLayout() {
  const [fontsLoaded, fontError] = useFonts({
    Geist_400Regular,
    Geist_500Medium,
    Geist_600SemiBold,
    GeistMono_500Medium,
    GeistMono_600SemiBold,
  });

  const sessionKnown = useSession((s) => s.status !== 'unknown');

  useEffect(() => {
    void restoreSession();
  }, []);

  // The splash covers font loading and session restore, so the first frame is already the right group.
  useEffect(() => {
    if ((fontsLoaded || fontError) && sessionKnown) {
      void SplashScreen.hideAsync();
    }
  }, [fontsLoaded, fontError, sessionKnown]);

  // A failed font load falls back to system fonts rather than blocking the app.
  if (!fontsLoaded && !fontError) {
    return null;
  }

  return (
    <GestureHandlerRootView style={{ flex: 1, backgroundColor: color.bg }}>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <BottomSheetModalProvider>
            <StatusBar style="light" />
            <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: color.bg } }} />
            <ToastHost />
          </BottomSheetModalProvider>
        </QueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}
