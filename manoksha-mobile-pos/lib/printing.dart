import 'package:esc_pos_utils_plus/esc_pos_utils_plus.dart';
import 'package:intl/intl.dart';
import 'package:permission_handler/permission_handler.dart';
import 'package:print_bluetooth_thermal/print_bluetooth_thermal.dart';

import 'app_state.dart';

String rs(num v) => 'Rs.${NumberFormat('#,##,##0.00', 'en_IN').format(v)}';

/// Prints a store receipt on a paired Bluetooth ESC/POS thermal printer (58 or 80 mm).
class ReceiptPrinter {
  static Future<bool> ensurePermissions() async {
    final results = await [Permission.bluetoothConnect, Permission.bluetoothScan].request();
    return results.values.every((s) => s.isGranted || s.isLimited);
  }

  static Future<List<BluetoothInfo>> pairedPrinters() async {
    if (!await ensurePermissions()) return [];
    return PrintBluetoothThermal.pairedBluetooths;
  }

  /// Returns null on success, or a message for staff.
  static Future<String?> printReceipt(Map<String, dynamic> r) async {
    final app = AppState.instance;
    if (app.printerMac == null) return 'No printer selected. Choose one in Settings.';
    if (!await ensurePermissions()) return 'Bluetooth permission is needed to print.';
    if (!await PrintBluetoothThermal.bluetoothEnabled) return 'Turn on Bluetooth to print.';
    if (!await PrintBluetoothThermal.connectionStatus) {
      final ok = await PrintBluetoothThermal.connect(macPrinterAddress: app.printerMac!);
      if (!ok) return 'Could not connect to ${app.printerName ?? 'the printer'}. Is it on and paired?';
    }
    final bytes = await _build(r, app.paper80mm ? PaperSize.mm80 : PaperSize.mm58);
    return await PrintBluetoothThermal.writeBytes(bytes) ? null : 'Printing failed. Try again.';
  }

  static Future<String?> printTest() => printReceipt({
    'shopName': 'Manoksha Collections',
    'branchName': 'Printer test',
    'number': 'TEST',
    'soldAt': DateTime.now().toUtc().toIso8601String(),
    'cashier': AppState.instance.displayName,
    'lines': [
      {'name': 'Test item', 'skuCode': 'TEST', 'quantity': 1, 'retailUnitPrice': 100, 'finalUnitPrice': 100, 'lineTotal': 100},
    ],
    'retailTotal': 100,
    'discountTotal': 0,
    'grandTotal': 100,
    'payments': [
      {'method': 'UPI', 'amount': 100, 'reference': 'TEST'},
    ],
  });

  static Future<List<int>> _build(Map<String, dynamic> r, PaperSize paper) async {
    final g = Generator(paper, await CapabilityProfile.load());
    final soldAt = DateTime.parse(r['soldAt'] as String).toLocal();
    final bold = const PosStyles(bold: true);
    var b = <int>[];
    b += g.reset();
    b += g.text(
      r['shopName'] as String,
      styles: const PosStyles(align: PosAlign.center, bold: true, height: PosTextSize.size2, width: PosTextSize.size1),
    );
    b += g.text(r['branchName'] as String, styles: const PosStyles(align: PosAlign.center));
    b += g.hr();
    b += g.text('Bill: ${r['number']}');
    b += g.text('Date: ${DateFormat('dd MMM yyyy, hh:mm a').format(soldAt)}');
    b += g.text('Cashier: ${r['cashier']}');
    if (r['customerName'] != null) b += g.text('Customer: ${r['customerName']}');
    b += g.hr();
    for (final l in (r['lines'] as List).cast<Map<String, dynamic>>()) {
      b += g.text(l['name'] as String, styles: bold);
      final discounted = (l['finalUnitPrice'] as num) < (l['retailUnitPrice'] as num);
      b += g.row([
        PosColumn(text: '${l['quantity']} x ${rs(l['finalUnitPrice'] as num)}${discounted ? ' (MRP ${rs(l['retailUnitPrice'] as num)})' : ''}', width: 8),
        PosColumn(
          text: rs(l['lineTotal'] as num),
          width: 4,
          styles: const PosStyles(align: PosAlign.right),
        ),
      ]);
    }
    b += g.hr();
    if ((r['discountTotal'] as num) > 0) {
      b += g.row([
        PosColumn(text: 'You saved', width: 6),
        PosColumn(
          text: rs(r['discountTotal'] as num),
          width: 6,
          styles: const PosStyles(align: PosAlign.right),
        ),
      ]);
    }
    b += g.row([
      PosColumn(
        text: 'TOTAL',
        width: 6,
        styles: const PosStyles(bold: true, height: PosTextSize.size2),
      ),
      PosColumn(
        text: rs(r['grandTotal'] as num),
        width: 6,
        styles: const PosStyles(bold: true, height: PosTextSize.size2, align: PosAlign.right),
      ),
    ]);
    for (final p in (r['payments'] as List).cast<Map<String, dynamic>>()) {
      b += g.text('${p['method']} ${rs(p['amount'] as num)}${p['reference'] != null ? '  Ref ${p['reference']}' : ''}');
    }
    b += g.hr();
    b += g.text('Thank you for shopping with us!', styles: const PosStyles(align: PosAlign.center));
    b += g.feed(3);
    b += g.cut();
    return b;
  }
}
