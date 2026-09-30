import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../api.dart';
import '../app_state.dart';
import '../printing.dart';
import 'common.dart';
import 'receipt_screen.dart';

class SalesTodayScreen extends StatefulWidget {
  const SalesTodayScreen({super.key});

  @override
  State<SalesTodayScreen> createState() => _SalesTodayScreenState();
}

class _SalesTodayScreenState extends State<SalesTodayScreen> {
  List<Map<String, dynamic>>? _sales;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final r = await Api.instance.get('pos/sales/today?branchId=${AppState.instance.branch!.id}') as List;
      if (mounted) setState(() => _sales = r.cast<Map<String, dynamic>>());
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _open(Map<String, dynamic> s) async {
    try {
      final receipt = await Api.instance.get('pos/sales/${s['orderId']}/receipt') as Map<String, dynamic>;
      if (mounted) await Navigator.of(context).push(MaterialPageRoute(builder: (_) => ReceiptScreen(receipt: receipt)));
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) {
    final sales = _sales;
    final total = sales?.fold<num>(0, (t, s) => t + (s['grandTotal'] as num)) ?? 0;
    return Scaffold(
      appBar: AppBar(title: const Text('Today\'s sales')),
      body: sales == null
          ? const Busy()
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                children: [
                  ListTile(
                    title: Text('${sales.length} sales'),
                    trailing: Text(rs(total), style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
                  ),
                  const Divider(height: 1),
                  for (final s in sales)
                    ListTile(
                      title: Text(s['number'] as String),
                      subtitle: Text(
                        '${DateFormat('hh:mm a').format(DateTime.parse(s['soldAt'] as String).toLocal())} · ${s['items']} items'
                        '${s['cashierUserId'] == AppState.instance.userId ? ' · you' : ''}',
                      ),
                      trailing: Text(rs(s['grandTotal'] as num)),
                      onTap: () => _open(s),
                    ),
                ],
              ),
            ),
    );
  }
}
