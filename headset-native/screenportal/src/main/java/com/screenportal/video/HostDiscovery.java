package com.screenportal.video;

import android.content.Context;
import android.net.nsd.NsdManager;
import android.net.nsd.NsdServiceInfo;
import android.net.wifi.WifiManager;

import java.net.InetAddress;

/**
 * Descobre o host "_screenportal._tcp" (mDNS/DNS-SD) com o NsdManager do Android.
 * A Unity chama poll() periodicamente; o resultado é "nome|ip|porta" do último host resolvido.
 *
 * Em Java (não Kotlin) de propósito: o .aar não embarca a kotlin-stdlib, e lógica em Kotlin gera
 * referências a Intrinsics/StringsKt/Unit que quebrariam em runtime (ADR 0006).
 */
public final class HostDiscovery {
    private static final String SERVICE_TYPE = "_screenportal._tcp";

    private static final Object LOCK = new Object();
    private static NsdManager nsd;
    private static WifiManager.MulticastLock multicastLock;
    private static boolean resolving;
    private static volatile String found;

    private HostDiscovery() {}

    private static final NsdManager.DiscoveryListener DISCOVERY = new NsdManager.DiscoveryListener() {
        @Override public void onDiscoveryStarted(String serviceType) {}
        @Override public void onDiscoveryStopped(String serviceType) {}
        @Override public void onStartDiscoveryFailed(String serviceType, int errorCode) {}
        @Override public void onStopDiscoveryFailed(String serviceType, int errorCode) {}

        @Override
        public void onServiceFound(NsdServiceInfo info) {
            resolve(info);
        }

        @Override
        public void onServiceLost(NsdServiceInfo info) {
            String current = found;
            if (current != null && current.startsWith(info.getServiceName() + "|")) found = null;
        }
    };

    @SuppressWarnings("deprecation")
    private static void resolve(NsdServiceInfo info) {
        NsdManager manager;
        synchronized (LOCK) {
            manager = nsd;
            if (manager == null || resolving) return;
            resolving = true;
        }
        manager.resolveService(info, new NsdManager.ResolveListener() {
            @Override
            public void onResolveFailed(NsdServiceInfo serviceInfo, int errorCode) {
                synchronized (LOCK) { resolving = false; }
            }

            @Override
            public void onServiceResolved(NsdServiceInfo serviceInfo) {
                InetAddress host = serviceInfo.getHost();
                if (host != null) {
                    found = serviceInfo.getServiceName() + "|" + host.getHostAddress() + "|" + serviceInfo.getPort();
                }
                synchronized (LOCK) { resolving = false; }
            }
        });
    }

    public static void start(Context context) {
        synchronized (LOCK) {
            if (nsd != null) return;
            Context app = context.getApplicationContext();
            WifiManager wifi = (WifiManager) app.getSystemService(Context.WIFI_SERVICE);
            if (wifi != null) {
                multicastLock = wifi.createMulticastLock("screenportal-mdns");
                multicastLock.setReferenceCounted(false);
                multicastLock.acquire();
            }
            nsd = (NsdManager) app.getSystemService(Context.NSD_SERVICE);
            if (nsd != null) nsd.discoverServices(SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, DISCOVERY);
        }
    }

    public static void stop() {
        synchronized (LOCK) {
            if (nsd != null) {
                try { nsd.stopServiceDiscovery(DISCOVERY); } catch (IllegalArgumentException ignored) {}
                nsd = null;
            }
            if (multicastLock != null) {
                multicastLock.release();
                multicastLock = null;
            }
            found = null;
            resolving = false;
        }
    }

    public static String poll() {
        return found;
    }
}
