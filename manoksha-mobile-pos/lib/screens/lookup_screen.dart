import 'package:flutter/material.dart';

import '../api.dart';
import '../printing.dart';
import 'common.dart';
import 'scanner_screen.dart';

/// Item lookup: product, price, this piece's status and where stock is (SPEC §19.2).
class LookupScreen extends StatefulWidget {
  const LookupScreen({super.key});

  @override
  State<LookupScreen> createState() => _LookupScreenState();
}

class _LookupScreenState extends State<LookupScreen> {
  final _code = TextEditingController();
  Map<String, dynamic>? _r;

  Future<void> _find(String code) async {
    _code.clear();
    if (code.trim().isEmpty) return;
    try {
      final r = await Api.instance.get('pos/scan/${Uri.encodeComponent(code.trim())}') as Map<String, dynamic>;
      if (mounted) setState(() => _r = r);
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) {
    final r = _r;
    return Scaffold(
      appBar: AppBar(title: const Text('Item lookup')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _code,
                  autofocus: true,
                  decoration: const InputDecoration(labelText: 'Scan or type barcode'),
                  onSubmitted: _find,
                ),
              ),
              IconButton.filledTonal(
                icon: const Icon(Icons.photo_camera),
                onPressed: () async {
                  final c = await Navigator.of(context).push<String>(MaterialPageRoute(builder: (_) => const ScannerScreen()));
                  if (c != null) await _find(c);
                },
              ),
            ],
          ),
          if (r != null) ...[
            const SizedBox(height: 16),
            Text(r['productName'] as String, style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold)),
            Text('${r['variantName']} · ${r['skuCode']}'),
            for (final a in (r['attributes'] as List).cast<Map<String, dynamic>>()) Text('${a['attributeName']}: ${a['value']}'),
            const SizedBox(height: 8),
            Text(r['retailPrice'] == null ? 'No price set' : 'MRP ${rs(r['retailPrice'] as num)}', style: const TextStyle(fontSize: 18)),
            Text('Status: ${r['productStatus']}${r['availableForRetail'] == true ? '' : ' · not for store sale'}'),
            if (r['inventoryItemId'] != null) Text('This piece: ${r['itemStatus']} at ${r['itemBranchName'] ?? '—'}'),
            const Divider(),
            const Text('Available by branch', style: TextStyle(fontWeight: FontWeight.bold)),
            for (final b in (r['availability'] as List).cast<Map<String, dynamic>>())
              ListTile(dense: true, contentPadding: EdgeInsets.zero, title: Text(b['branchName'] as String), trailing: Text('${b['available']}')),
          ],
        ],
      ),
    );
  }
}
