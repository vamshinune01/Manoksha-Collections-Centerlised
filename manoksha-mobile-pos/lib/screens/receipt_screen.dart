import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../app_state.dart';
import '../printing.dart';
import 'common.dart';

class ReceiptScreen extends StatelessWidget {
  const ReceiptScreen({super.key, required this.receipt});

  final Map<String, dynamic> receipt;

  @override
  Widget build(BuildContext context) {
    final r = receipt;
    final soldAt = DateTime.parse(r['soldAt'] as String).toLocal();
    return Scaffold(
      appBar: AppBar(title: Text(r['number'] as String)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text('${r['shopName']} · ${r['branchName']}', style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
          Text('${DateFormat('dd MMM yyyy, hh:mm a').format(soldAt)} · ${r['cashier']}'),
          if (r['customerName'] != null || r['customerMobile'] != null) Text('Customer: ${r['customerName'] ?? ''} ${r['customerMobile'] ?? ''}'),
          const Divider(),
          for (final l in (r['lines'] as List).cast<Map<String, dynamic>>())
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text(l['name'] as String),
              subtitle: Text(
                '${l['quantity']} × ${rs(l['finalUnitPrice'] as num)}'
                '${(l['finalUnitPrice'] as num) < (l['retailUnitPrice'] as num) ? ' (MRP ${rs(l['retailUnitPrice'] as num)})' : ''}',
              ),
              trailing: Text(rs(l['lineTotal'] as num)),
            ),
          const Divider(),
          if ((r['discountTotal'] as num) > 0) Text('Discount: ${rs(r['discountTotal'] as num)}'),
          if (r['approvedBy'] != null) Text('Approved by ${r['approvedBy']}'),
          Text('Total ${rs(r['grandTotal'] as num)}', style: const TextStyle(fontSize: 22, fontWeight: FontWeight.bold)),
          const SizedBox(height: 8),
          for (final p in (r['payments'] as List).cast<Map<String, dynamic>>())
            Text('${p['method']} ${rs(p['amount'] as num)}${p['reference'] != null ? ' · ref ${p['reference']}' : ''}'),
          const SizedBox(height: 24),
          FilledButton.icon(
            onPressed: AppState.instance.printerMac == null
                ? null
                : () async {
                    final err = await ReceiptPrinter.printReceipt(r);
                    if (context.mounted) err == null ? showInfo(context, 'Printed.') : showInfo(context, err);
                  },
            icon: const Icon(Icons.print),
            label: Text(AppState.instance.printerMac == null ? 'No printer set up (see Settings)' : 'Print receipt'),
          ),
          const SizedBox(height: 8),
          OutlinedButton(onPressed: () => Navigator.pop(context), child: const Text('Done')),
        ],
      ),
    );
  }
}
