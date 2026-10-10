package com.nugship.picker;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.ImageDecoder;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

/** NUGSHIP avatar upload: system photo picker -> centre square crop -> 128x128 PNG in the app cache. Polled from C#. */
public class PickerActivity extends Activity {
    private static final int REQ = 7311;
    private static volatile boolean busy;
    private static volatile String result; // "" cancelled, "!err" failure, else PNG path

    /** Called from Unity (AvatarPicker.cs). */
    public static void pick(Activity from) {
        busy = true; result = null;
        from.startActivity(new Intent(from, PickerActivity.class));
    }

    /** null while the picker is open; then the result exactly once. */
    public static String poll() {
        if (busy) return null;
        String r = result; result = null;
        return r == null ? null : r;
    }

    @Override protected void onCreate(Bundle b) {
        super.onCreate(b);
        if (b != null) return; // recreated after a config change: the picker is already showing
        Intent i;
        if (Build.VERSION.SDK_INT >= 33) { i = new Intent(MediaStore.ACTION_PICK_IMAGES); i.setType("image/*"); }
        else { i = new Intent(Intent.ACTION_GET_CONTENT); i.setType("image/*"); i.addCategory(Intent.CATEGORY_OPENABLE); }
        try { startActivityForResult(i, REQ); }
        catch (Exception e) { done("!" + e); }
    }

    @Override protected void onActivityResult(int req, int code, Intent data) {
        super.onActivityResult(req, code, data);
        if (req != REQ) return;
        if (code != RESULT_OK || data == null || data.getData() == null) { done(""); return; }
        try { done(save(decode(data.getData()))); }
        catch (Throwable t) { done("!" + t); }
    }

    private Bitmap decode(Uri uri) throws Exception {
        ContentResolver cr = getContentResolver();
        if (Build.VERSION.SDK_INT >= 28) {
            return ImageDecoder.decodeBitmap(ImageDecoder.createSource(cr, uri), (dec, info, src) -> {
                int s = Math.max(1, Math.min(info.getSize().getWidth(), info.getSize().getHeight()) / 512);
                dec.setTargetSampleSize(s);
                dec.setAllocator(ImageDecoder.ALLOCATOR_SOFTWARE);
            }); // applies EXIF orientation
        }
        BitmapFactory.Options o = new BitmapFactory.Options(); o.inJustDecodeBounds = true;
        try (InputStream in = cr.openInputStream(uri)) { BitmapFactory.decodeStream(in, null, o); }
        o.inSampleSize = Math.max(1, Math.min(o.outWidth, o.outHeight) / 512); o.inJustDecodeBounds = false;
        try (InputStream in = cr.openInputStream(uri)) { return BitmapFactory.decodeStream(in, null, o); }
    }

    private String save(Bitmap bmp) throws Exception {
        if (bmp == null) return "!decode failed";
        int s = Math.min(bmp.getWidth(), bmp.getHeight());
        Bitmap sq = Bitmap.createBitmap(bmp, (bmp.getWidth() - s) / 2, (bmp.getHeight() - s) / 2, s, s);
        Bitmap out = Bitmap.createScaledBitmap(sq, 128, 128, true);
        File f = new File(getCacheDir(), "avatar_pick.png");
        try (FileOutputStream fo = new FileOutputStream(f)) { out.compress(Bitmap.CompressFormat.PNG, 100, fo); }
        return f.getAbsolutePath();
    }

    private void done(String r) { result = r; busy = false; finish(); overridePendingTransition(0, 0); }

    @Override public void onBackPressed() { done(""); }

    @Override protected void onDestroy() { super.onDestroy(); if (busy && isFinishing()) { result = ""; busy = false; } }
}
